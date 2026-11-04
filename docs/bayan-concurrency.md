# Bayān — Concurrency design

Nov 1, 2026 · Confirmed design, to be specced in round 4 and implemented in v0.2. Nothing here changes 010–013.

Examples use the proposed round-4 clause order (`where → expects → uses → becomes → fails`). Cancel words use US spelling (`canceled`, `canceling`).

## Relation to the spec

This reopens Appendix A's `async`/`await` (user decision 2026-11-04): `scope`/`spawn` replaces `async`/`await` and `Io.group()`. Round 4 removes `async`/`await`/`Io.group` from spec §13 and Appendix A by revision.

The block deadline is `scope(timeout:)` / `until:` (user decision 2026-11-04). Round 4 removes `provide timeout: d { … }` (spec §13 :1556), which also clashes with BYN6007.

## 1. Decisions

| # | Decision | Built-in remedies |
| --- | --- | --- |
| R1 | One worker thread per core; fibers never move between threads | Accept-time balancing, debug watchdog, `Fiber.yield()`, one worker for `bayan run` |
| R2 | `spawn` only inside `scope` | `limit:`, `policy: .collectAll`, supervisor scope in `main`, channels for background jobs; no detached tasks |
| R3 | Cancellation is an implicit unwind at suspend points | `shield { }`, `defer canceling`, shielded `defer` bodies with a grace timeout; cancellation runs `defer failing` |
| R4 | Auto-derived `ISendable` / `IShareable` | Errors show the field path, `@sendable` locks a public type, `@sendable because "…"` for checked C handles |
| R5 | No OS lock held across a suspend point | `Mutex.with` whose closure may not suspend; `AsyncMutex` for I/O serialization |
| R6 | `@blocking` C calls go to a blocking pool; CPU work is offloaded | Watchdog names the stalling extern, `bayan audit` lists unannotated externs, bounded pool |
| R7 | Resources (database, HTTP, files) are owned per worker and per child | Section 8 |

## 2. Where fibers run

Each core runs one worker thread with its own event loop. A fiber stays on the worker that started it, so fibers on one worker never race: `Rc`, arenas and `@threadLocal` data are safe without locks.

```cs
void main() uses net, suspend, alloc fails NetError {
    let server = try Http.listen(at: 8080, workers: .perCore)
    try server.serve(by: handle)        // each request runs on the worker that accepted it
}

@threadLocal var sessions: Map<SessionId, Session> = []   // one map per worker, no lock

Response handle(Request req) uses net, suspend, alloc fails HttpError {
    let session = sessions.tryGet(req.sessionId) ?? return Response.unauthorized
    …
}
```

| Risk | Remedy |
| --- | --- |
| Heavy connections pile onto one worker | New connections go to the least-loaded worker; CPU work is offloaded (section 7) |
| A loop without suspend points starves other fibers | Debug watchdog reports fibers running past a threshold; `Fiber.yield()` |
| Per-worker caches duplicate memory | Shard ownership by key and ask the owner by channel, or share with `Arc<Mutex<T>>` |
| Overkill for scripts | `bayan run` uses one worker |

## 3. `scope` and `spawn`

`spawn` is legal only inside a `scope` block. The scope waits for every child before it exits, so no task outlives its caller and children may borrow the parent's locals. While a child holds `ref` to a parent local, the parent cannot use it until the scope ends. The first failure cancels the siblings and propagates; the scope's `fails` list is the union of its children's. `scope` is a block, not a closure, so `return`, `try` and `break` keep their meaning.

```cs
Summary dashboard(UserId id, from Pool pool) uses net, suspend fails DbError {
    scope(timeout: 2s) {
        let profile = spawn loadProfile(id, from: pool)
        let orders  = spawn loadOrders(id, from: pool)
        return Summary { profile: try profile.join(), orders: try orders.join() }
    }
}
```

**`limit:` caps how many children run at once, not the total.** When the cap is reached, `spawn` parks the parent; when any child finishes, the parent resumes and spawns the next. Memory stays at the cap, not the input size, and the loop gets backpressure for free. Children start in loop order and finish in any order.

```cs
void syncImages(ReadOnlySpan<Product> products) uses net, fs, suspend fails NetError, IoError {
    scope(limit: 16, timeout: 60s) {
        for product in products {
            spawn download(product.imageUrl, to: cacheDir / product.sku)
        }
    }
}
```

To keep results without holding every task handle, a child writes into its own slot of a preallocated list or sends to a channel the parent reads.

Options:

| Option | Meaning |
| --- | --- |
| `limit: n` | At most `n` children run at once |
| `timeout: d` / `until: t` | Cancel the scope after a duration or at a deadline; covered by `suspend`, no `clock` needed |
| `policy: .collectAll` | No sibling is canceled on failure; each `join()` returns its own result |
| `workers: .compute` | Children run on the compute pool; arguments must be `ISendable` |

Long-lived work lives in a supervisor scope; one-off background work goes through a channel, never a detached task:

```cs
void main() uses net, suspend, alloc fails NetError {
    let (jobs, inbox) = Channel<Job>.open(capacity: 4096)
    scope {
        spawn flushMetrics(interval: 10s)
        spawn runJobs(from: inbox)
        try App { jobs: jobs }.serve(at: 8080)
    }
}

// in a handler
try jobs.send(.welcomeEmail(user.id))
```

| Risk | Remedy |
| --- | --- |
| Too many children at once | `limit:` |
| Parent and children wait on each other | The body finishes before the scope waits; a debug detector reports a scope whose fibers are all parked on each other |
| First error hides the others | `policy: .collectAll` |

## 4. Cancellation

A canceled fiber stops at its next suspend point and unwinds, running `defer`, `defer failing` and `defer canceling`. Only `scope` observes it. Code between two suspend points is never interrupted. In C, this is one flag check after each suspending call, reusing the `fail` return path.

```cs
Receipt checkout(Cart cart, from Pool pool) uses net, suspend fails PayError, DbError {
    var conn = try pool.acquire()
    var tx = try conn.begin()
    defer failing tx.rollback()
    defer canceling log("checkout canceled", as: .warning)
    try reserveStock(cart, by: tx)
    shield {
        let charge = try payments.charge(cart.total, idempotencyKey: cart.idempotencyKey)
        try tx.commit()
        return Receipt(charge)
    }
}
```

Without the `shield`, a cancel between `charge` and `commit` would leave the card charged and the order rolled back. The idempotency key covers a process crash, which no language rule can.

| Risk | Remedy |
| --- | --- |
| A critical pair is split by a cancel | `shield { }` defers cancellation until the block ends |
| A shield waits forever | Each call inside keeps its own timeout; `bayan audit` lists shields containing calls without one |
| Cleanup needs the network after cancel | `defer` bodies run shielded with a grace timeout (default 5s) |
| Cancel arrives inside a C callback | Unwinding never crosses C frames; it waits until control returns to Bayān |

## 5. Sending between threads

Only channels, `workers: .compute` scopes and `Arc` cross threads. Two marker interfaces are derived from a type's fields; users never write them.

| Interface | Meaning | Not satisfied by |
| --- | --- | --- |
| `ISendable` | Can move to another thread | `Rc<T>`, borrows into a worker arena, `@threadLocal` data, worker-bound I/O handles |
| `IShareable` | Can be read from several threads at once | Unsynchronized mutable state; `Mutex<T>` and `Atomic<T>` qualify |

```cs
var thumbnail: Image
scope(workers: .compute) {
    let job = spawn resize(move image, to: 256px)
    thumbnail = try job.join()
}
// definitely assigned here: `scope` is a block that runs once, not a closure (BYN2102 otherwise)

scope(workers: .compute) {
    spawn render(session)
    // error: `Session` can't move to another thread: `Session.cache` is `Rc<UserCache>`
}

@sendable because "libsodium state is thread-safe after sodium_init"
struct unique Hasher {
    SodiumState state
}
```

`@sendable` on a public type turns an accidental loss of sendability into an error in the library, not in its users. `bayan audit` reports sendability changes between versions.

## 6. Locks

Holding an OS lock while a fiber parks deadlocks the worker, so it is an error. The block form makes the check exact: the closure passed to `with` has an effect bound that excludes `suspend`.

```cs
var buckets: Mutex<Map<ApiKey, Bucket>> = Mutex([])     // module level

bool allow(ApiKey key) uses net, suspend fails NetError {
    let plan = try billing.plan(for: key)                 // I/O first
    return buckets.with((ref map) => map[key].take(for: plan))   // short, cannot suspend
}
```

`AsyncMutex<T>` parks the fiber instead of the thread and may be held across suspend points. On one worker, the remaining hazard is a stale write (read, suspend, write back); mutate before suspending. A lint for it comes later.

## 7. Blocking calls and CPU work

`@blocking` externs run on a bounded blocking pool so the worker keeps serving fibers. Unannotated externs run inline. CPU-heavy work uses `scope(workers: .compute)`.

```cs
extern c {
    @blocking c_int sqlite3_step(SqliteStmt* stmt)
    c_int sqlite3_column_int(SqliteStmt* stmt, c_int col)
}
```

The pool size is set in `package.byn`; when it is full, callers park.

## 8. Databases and other resources

Thread-per-core changes how pooled resources work: each worker owns its own connections, and each child acquires its own.

### 8.1 Connections are per worker and not sendable

A Postgres connection's socket is registered with one worker's event loop, so the connection is not `ISendable`. Each worker keeps its own pool. The total is a budget set once and split across workers, so 32 cores do not open 32 full pools:

```cs
// package.byn
db: Database {
    connections: 64,      // total across all workers; each worker gets 64 / workers
}
```

Behind PgBouncer or a managed proxy, the same budget applies to the proxy's limits.

### 8.2 Children cannot share one connection

A connection runs one query at a time. Two children taking `ref conn` is two mutable borrows, so it fails at compile time. In C#, Npgsql reports this at runtime with `NpgsqlOperationInProgressException`.

```cs
scope {
    spawn loadOrders(ref conn, id)
    spawn loadProfile(ref conn, id)    // error: `conn` is already borrowed by another child
}
```

Each child acquires its own from the worker's pool. `acquire()` parks the fiber when the pool is empty. `Pool` is a handle that every child can hold at once.

```cs
List<Order> loadOrders(UserId id, from Pool pool) uses net, suspend fails DbError {
    var conn = try pool.acquire()          // returned to the pool when `conn` goes out of scope
    return try conn.query<Order>(sql"SELECT … WHERE user_id = $1", id).all()
}
```

With `scope(limit: 16)` and 10 pooled connections, 6 children wait on `acquire()`. That is correct, but their wait counts against the scope timeout. Set `limit` at or below the worker's share of connections.

### 8.3 A transaction stays with one fiber

A transaction is `unique` and lives on one connection, so children cannot run inside the parent's transaction. That matches the database: one connection executes one statement at a time. Children compute; the parent writes.

```cs
scope {
    let prices = spawn quotePrices(cart)          // no database work
    let stock  = spawn checkStock(cart, from: pool)
    let (p, s) = (try prices.join(), try stock.join())
    try tx.execute(sql"INSERT …", p.total, s.reservationId)   // the parent owns `tx`
}
```

There is no ambient "current transaction". C#'s `TransactionScope` needs `TransactionScopeAsyncFlowOption.Enabled` to survive `await`, and forgetting it silently drops the transaction. In Bayān the transaction is a value you pass.

### 8.4 Cancellation in the middle of a query

A fiber canceled while waiting for a result leaves the server still running the query and results in flight. Returning that connection to the pool would hand stale results to the next user. A pooled connection therefore tracks its state, and drop does the right thing per state:

| State at drop | Action |
| --- | --- |
| `.idle` | Return to the pool |
| `.inTransaction` | Roll back (shielded, grace timeout), then return |
| `.busy` (query in flight) | Send a cancel request (Postgres CancelRequest, `sqlite3_interrupt`), then discard and open a replacement later |

```cs
struct unique PooledConnection {
    states {
        idle
        busy
        inTransaction { TxId tx }
    }
}
```

### 8.5 Holding a connection too long

Holding a pooled connection across unrelated slow I/O starves the pool, the most common pool bug in any language. Acquire late, release early. The runtime reports connection hold time per call site, and the watchdog flags connections held past a threshold.

### 8.6 SQLite

SQLite allows one writer at a time, and its calls block. Two shapes fit:

| Shape | How | When |
| --- | --- | --- |
| Writer thread | One dedicated thread owns the write connection; workers send requests over a channel and park until done. Each worker keeps its own read connection (WAL mode). | Services with concurrent writes |
| Blocking pool | Mark the calls `@blocking`; the connection must then be `@sendable because` SQLite runs in serialized mode | Small tools and scripts |

```cs
let writer = try Sqlite.writer(at: "app.db")                        // owns the write connection
try writer.execute(sql"INSERT INTO orders …", order.id)             // sends, parks until done
let items = try readers.local().query<Item>(sql"SELECT …").all()    // this worker's read connection
```

### 8.7 Other resources

| Resource | Rule |
| --- | --- |
| HTTP client keep-alive pools | Per worker, with a total budget per upstream host, as for databases |
| External rate limits | Global across workers: an `Atomic` token bucket or the `Mutex.with` pattern in section 6 |
| Files and sockets | Registered with one worker's event loop; not sendable. Pass the data, not the handle. |
| Memory | Each spawned child gets a child arena, freed when it ends unless its result escapes |

## 9. Syntax and diagnostics

New syntax: `scope`, `spawn`, `shield`, `defer canceling`, `@sendable`.

New diagnostics (numbers assigned at the round-4 renumbering, area 6xxx unless noted):

| Diagnostic | Notes |
| --- | --- |
| Spawn outside scope | Replaces most of BYN3217 |
| Not sendable (with field path) | |
| Lock held across a suspend point | |
| Suspend inside `with` | |
| `@sendable` contract broken | |
| Connection borrowed by two children | Ordinary borrow error (3xxx) with a concurrency-specific help line |

## 10. Timing and open items

- Spec in round 4; runtime in v0.2. The self-hosted compiler needs none of this.
- Later: `select` over channels (could reuse the subjectless `switch` shape), the stale-write lint, WASM support once stack switching ships (until then, `uses suspend` is an error on `wasm32`).
- Decide during Sūq: test implicit cancellation against explicit `fails .canceled` on `checkout`, a websocket handler and a file upload.

## 11. Failure isolation

Added after the October 2026 PHP2Shell flaws, where an out-of-memory `longjmp` left request data half-built and a later request on the same worker read stale state.

| Rule | Why |
| --- | --- |
| No `longjmp`-style aborts; `fail` and cancellation return through ordinary code paths, at suspend points only | Cleanup never sees half-initialized data |
| Out of memory is a panic that ends the process | No attempt to clean up and keep serving |
| After a panic, the process exits and the supervisor restarts it; workers are never reused | State from a failed request cannot reach the next one |
| Request data lives in the request arena; `@threadLocal` (per-worker) data cannot hold references into it | Enforced by region checking |
