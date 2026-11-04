# Bayān — Concurrency design

Nov 1, 2026 · Confirmed design, to be specced in round 4 and implemented in v0.2. Nothing here changes 010–013.

Examples use the proposed round-4 clause order (`where → expects → uses → becomes → fails`). Cancel words use US spelling (`canceled`, `canceling`).

## Relation to the spec

This reopens Appendix A's `async`/`await` (user decision 2026-11-04): `scope`/`spawn` replaces `async`/`await` and `Io.group()`. Round 4 removes `async`/`await`/`Io.group` from spec §13 and Appendix A by revision.

The block deadline is `scope(timeout:)` / `until:` (user decision 2026-11-04). Round 4 removes `provide timeout: d { … }` (spec §13 :1556), which also clashes with BYN6007.

## 1. Decisions

| # | Decision | Built-in remedies |
| --- | --- | --- |
| R1 | One worker thread per core; fibers never move between threads | Accept-time balancing, debug watchdog, `Fiber.yieldNow()`, one worker for `bayan run` |
| R2 | `spawn` only inside `scope` | `limit:`, `policy: .collectAll`, supervisor scope in `main`, channels for background jobs; no detached tasks |
| R3 | Cancellation is an implicit unwind at suspend points; `Canceled` is not an error value | `shield(timeout:) { }`, `defer canceling`, `defer` bodies run as a shield; `Fiber.checkCanceled()` for CPU loops; cancellation runs `defer failing` |
| R4 | Auto-derived `ISendable` / `IShareable` | Errors show the field path, `@sendable` locks a public type, `@sendable because "…"` (non-empty) for checked C handles; replaces the `unsafe` marker |
| R5 | No OS lock held across a suspend point | Guards (`let map = buckets.lock()`) checked across suspend points, released at block end or by `map.unlock()`; `AsyncMutex` for I/O serialization |
| R6 | `@blocking` C calls go to a blocking pool; CPU work runs in `scope(workers: .compute)` | Watchdog names the stalling extern, `bayan audit` lists unannotated externs, bounded pool |
| R7 | Resources (database, HTTP, files) are owned per worker and per child | Section 8 |

## 2. Where fibers run

Each core runs one worker thread with its own event loop. This is `Io.Evented(workers: .perCore)`; `Io.Evented(workers: n)` sets the count. `Io` stays the one entry point (`context.Io`, spec §13.1), with three implementations: `Io.Evented`, `Io.Blocking` and `Io.Simulated`. `Io.Threaded` is removed. A fiber stays on the worker that started it, so fibers on one worker never race: `Rc`, arenas and `@threadLocal` data are safe without locks.

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
| Heavy connections pile onto one worker | New connections go to the least-loaded worker; CPU work goes to `scope(workers: .compute)` (section 7) |
| A loop without suspend points starves other fibers | Debug watchdog reports fibers running past a threshold; `Fiber.yieldNow()` |
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
| `workers: .compute` | Children run on the compute pool; arguments must be `ISendable`. The only CPU offload spelling (`Io.offload` is removed) |

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

Cancellation is not an error value: `Canceled` is not in any I/O error set, and a function never lists it in `fails`. This keeps `catch value` and `catch return` (spec §7) from swallowing a cancel: an unwind passes through every `catch`.

```cs
Receipt checkout(Cart cart, from Pool pool) uses net, suspend fails PayError, DbError {
    var conn = try pool.acquire()
    var tx = try conn.begin()
    defer failing tx.rollback()
    defer canceling log("checkout canceled", as: .warning)
    try reserveStock(cart, by: tx)
    shield(timeout: 10s) {
        let charge = try payments.charge(cart.total, idempotencyKey: cart.idempotencyKey)
        try tx.commit()
        return Receipt(charge)
    }
}
```

Without the `shield`, a cancel between `charge` and `commit` would leave the card charged and the order rolled back. The idempotency key covers a process crash, which no language rule can.

**`shield` is the one uncancelable region.** Inside it, a pending cancel waits until the block ends; suspend points do not unwind. Every shield has a timeout: `timeout:` (default 5s) bounds the block. Every `defer` body runs as an implicit `shield` with the default timeout, so cleanup that suspends (a rollback, a websocket close frame) does not unwind straight away. There is no shielded `defer` variant and no `scope` option for it.

**When a shield times out, its own region is canceled.** The next suspend point inside the shield unwinds, running the shield's inner `defer`s; it never panics. What follows depends on why the shield was waiting:

| At timeout | Result |
| --- | --- |
| A cancel is pending (the usual case: a shield delays it) | The unwind continues outward as that cancel; nothing new is reported |
| No cancel is pending | The shield fails with `TimeoutError`, as a `scope(timeout:)` does, so its `fails` list includes it |

A nested shield's timeout is capped at the enclosing shield's remaining time, so a shield inside a `defer` cannot outlast the `defer`.

**A cancel can be delayed by cleanup.** Because every `defer` body is a shield, an unwind that runs several suspending `defer` bodies waits for each in turn: up to their timeouts added together (5s each by default), one per frame with such a `defer`. That is accepted. The debug watchdog reports a cancel that takes longer than a threshold to finish, naming the slow `defer`.

**`scope(timeout:)` expiry fails the scope with `TimeoutError`.** The deadline belongs to the scope, so its caller can handle it; children see it as a cancel. A cancel from outside the scope is not a `TimeoutError`: it unwinds through the scope.

```cs
void close(ref Socket socket) uses net, suspend fails NetError {
    defer socket.release()
    shield(timeout: 2s) {
        try socket.send(closeFrame)       // runs even while the handler is being canceled
    }
}
```

**CPU-bound code checks by hand.** A fiber in a `.compute` loop with no suspend point never sees a cancel. `Fiber.checkCanceled()` is the one checkpoint: it unwinds if a cancel is pending and does nothing otherwise. `Fiber.yieldNow()` is a suspend point, so it also unwinds on a pending cancel: a loop that calls it needs no `checkCanceled()`. `checkCanceled()` is the cheap form that never reschedules. A `scope(timeout:)` deadline is a cancel, so it covers deadlines too and replaces spec §13.2's `context.checkDeadline()`.

```cs
void renderAll(ReadOnlySpan<Tile> tiles) {
    for tile in tiles {
        Fiber.checkCanceled()       // unwinds here once the 30s deadline passes
        render(tile)
    }
}

scope(workers: .compute, timeout: 30s) {
    spawn renderAll(tiles)
}
```

A 49xx lint (opt-in candidate, number at the round-4 renumbering) may flag a long loop in a `.compute` scope with neither a suspend point nor a checkpoint.

| Risk | Remedy |
| --- | --- |
| A critical pair is split by a cancel | `shield(timeout:) { }` defers cancellation until the block ends |
| A shield waits forever | Every shield has a timeout (default 5s); at timeout its region is canceled |
| Slow cleanup delays a cancel | Bounded by the `defer` timeouts along the unwind; the watchdog names the slow `defer` |
| Cleanup needs the network after cancel | `defer` bodies run as a `shield` with the default timeout |
| A CPU loop ignores a cancel | `Fiber.checkCanceled()`; candidate 49xx lint |
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

`@sendable because "…"` replaces spec §13.5's `unsafe` marker for types holding raw pointers or C handles. The reason must be non-empty, as for every `because` clause; `bayan audit` lists it with the other bypasses.

`@sendable` on a public type turns an accidental loss of sendability into an error in the library, not in its users. `bayan audit` reports sendability changes between versions.

## 6. Locks

Holding an OS lock while a fiber parks deadlocks the worker, so it is an error: a `Mutex` guard alive across a suspend point (spec §13.4). `lock()` returns a guard; the lock is released when the guard's block ends. There is no `Mutex.with` closure form, so there is no second lock spelling, no clash with the `with` keyword, and `return`/`try` keep their meaning inside the locked region.

```cs
var buckets: Mutex<Map<ApiKey, Bucket>> = Mutex([])     // module level

bool allow(ApiKey key) uses net, suspend fails NetError {
    let plan = try billing.plan(for: key)                 // I/O first
    let map = buckets.lock()
    return map[key].take(for: plan)                       // released at block end
}
```

Releasing early has one spelling, the consuming method `unlock(move this)`. Using the guard afterwards is an ordinary use-after-move error, with no new rule.

```cs
let map = buckets.lock()
let token = map[key].take(for: plan)
map.unlock()
try notify(key, token)                                    // suspends; the lock is already released
map.size                                                  // error: `map` was moved by `unlock()`
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
| External rate limits | Global across workers: an `Atomic` token bucket or the `Mutex` guard pattern in section 6 |
| Files and sockets | Registered with one worker's event loop; not sendable. Pass the data, not the handle. |
| Memory | Each spawned child gets a child arena, freed when it ends unless its result escapes |

## 9. Syntax and diagnostics

New syntax: `scope`, `spawn`, `shield(timeout:)`, `defer canceling`, `@sendable`. New library: `Fiber.checkCanceled()`, `MutexGuard.unlock(move this)`.

New diagnostics (numbers assigned at the round-4 renumbering, area 6xxx unless noted):

| Diagnostic | Notes |
| --- | --- |
| Spawn outside scope | Replaces most of BYN3217 |
| Not sendable (with field path) | |
| Lock held across a suspend point | Spec §13.4's guard rule, kept |
| `@sendable` contract broken | |
| Empty `because` on `@sendable` | Same rule as other `because` clauses |
| Long `.compute` loop without a checkpoint | Candidate lint, 49xx, opt-in |
| Connection borrowed by two children | Ordinary borrow error (3xxx) with a concurrency-specific help line |

## 10. Timing and open items

- Spec in round 4; runtime in v0.2. The self-hosted compiler needs none of this.
- Later: `select` over channels (could reuse the subjectless `switch` shape), the stale-write lint, WASM support once stack switching ships (until then, `uses suspend` is an error on `wasm32`).
- Implicit cancellation is decided (R3, user 2026-11-04). Sūq still exercises it on `checkout`, a websocket handler and a file upload; a failure there reopens R3 by revision.

## 11. Failure isolation

Added after the October 2026 PHP2Shell flaws, where an out-of-memory `longjmp` left request data half-built and a later request on the same worker read stale state.

| Rule | Why |
| --- | --- |
| No `longjmp`-style aborts; `fail` and cancellation return through ordinary code paths, at suspend points only | Cleanup never sees half-initialized data |
| Out of memory is a panic that ends the process | No attempt to clean up and keep serving |
| After a panic, the process exits and the supervisor restarts it; workers are never reused | State from a failed request cannot reach the next one |
| Request data lives in the request arena; `@threadLocal` (per-worker) data cannot hold references into it | Enforced by region checking |

## 12. Round-4 rewrite of spec §13

User decisions 2026-11-04 (review rows C4, C16–C19). The round-4 revision makes these spec edits:

| Spec site | Change | Row |
| --- | --- | --- |
| §13.1 table (:1537-1543) | Drop `Io.Threaded`. `Io.Evented(workers: .perCore \| n)` is the server default; `bayan run` and scripts use `Io.Evented(workers: 1)`. `Io.Blocking` and `Io.Simulated` stay. | C16 |
| §13.1 (:1545) | Keep "Libraries never choose an implementation". `#:io evented` / `#:io blocking` are the script overrides | C16 |
| §13.1, new | `Io.Blocking` runs `spawn` children in order, inline. A child that would wait on something only a later sibling can provide (a channel receive, a join) is a runtime panic naming both, never a silent hang. | C16 |
| §13.1, new | `workers: .compute` under `Io.Evented` is a separate pool from the I/O workers: default size one thread per core, set in `package.byn` like the blocking pool. Under `Io.Blocking` and `Io.Simulated` it runs inline. | C16 |
| §13.2 (:1547-1564) | Replace `async`/`await`/`Future` with `scope`/`spawn`/`join()` (C1). Remove "`Canceled` is in every I/O error set" (:1551); cancellation is an unwind, not an error value. Replace `provide timeout:` with `scope(timeout:)`/`until:`, and `context.checkDeadline()` with `Fiber.checkCanceled()`. | C1, C4 |
| §13.2, new | `shield(timeout: d) { }` is the one uncancelable region (default 5s); every `defer` body runs as one. At timeout the region is canceled: a pending cancel continues, otherwise `TimeoutError`. Nested shields are capped by the outer one. `scope(timeout:)` expiry fails the scope with `TimeoutError`. | C4 |
| §13.2, new | `Fiber.yieldNow()` (not `yield`, a reserved keyword) is a suspend point and unwinds on a pending cancel. | C4, I1 |
| §13.3 (:1565-1567) | Remove `Io.group()`; `scope` replaces it. | C1 |
| §13.4 (:1568-1575) | Keep the guard rule. Add `unlock(move this)` as the one early release. No `Mutex.with`. | C19 |
| §13.5 (:1580) | Replace the `unsafe` marker with `@sendable because "…"` (non-empty reason). | C17 |
| §13.5 (:1582) | Remove `Io.offload(op)`; `scope(workers: .compute)` replaces it. | C18 |
| §21 (:2279), §23 (:2379, :2383) | `bayan new --script` writes `#:io evented`, not `#:io threaded`; the script default `Io` is `Evented(workers: 1)`. | C16 |
| Appendix A | `scope`/`spawn` replaces `async`/`await` (C1, decision 2026-11-04). | C1 |

