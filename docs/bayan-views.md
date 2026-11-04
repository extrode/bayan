# Bayān — Markup and views

Nov 1, 2026 · Accepted as a provisional design. The mechanism is fixed in principle; details are expected to change once a real product is built with it. Syntax follows round 3 plus the round-4 clause order (`where → expects → uses → becomes → fails`). Library names under `kit.*` are illustrative.

## 1. Decisions

| # | Decision |
| --- | --- |
| V1 | `markup target { … }` is a generic language feature with no built-in vocabulary. Libraries (`kit.web`, `kit.ui`, `kit.mail`, `kit.term`, …) supply elements and targets. |
| V2 | Components are PascalCase structs marked `@element`; their fields are attributes (types, defaults, `requires`). Primitive elements are lowercase library functions marked `@element`. Attributes are passed by name, so `@element` is exempt from the particle rule. |
| V3 | Escaping belongs to the target and is decided by type. Raw output needs `because` and appears in `bayan audit`. |
| V4 | The `control` keyword from the `kit.ui` drafts is dropped: a control is an `@element` struct. |
| V5 | One-child blocks may sit on one line inside markup only (`h1 { product.name }`). |
| V6 | A line holding only an identifier is always a value. An element needs a block, an argument or a flag; write `title {}` for an empty element. |
| V7 | An element accepts children through a `render` parameter `by Children<Target> content`; a `content` line renders them. |
| V8 | A member derivation may add an `@element` static member with a fixed signature (e.g. `ReviewForm.fields`). |
| V9 | `kit.web` wraps any element as a full page (`Page`, which writes the doctype) or an HTMX partial (`Fragment`). |
| V10 | A `catch` arm may be a block if the block leaves (`return` or `fail`), the same rule as `catch switch` arms (J49). |
| V11 | Reserve the keyword `markup` in round 4. The feature itself is additive and lands in v0.2 with `kit.web` and `kit.ui`. |

## 2. Grammar inside `markup`

The parser uses markup grammar only inside `markup target { … }` and in the children blocks nested in it. Each line is one of:

| Line | Meaning |
| --- | --- |
| `name positional name: value flag { children }` | An element. No commas. Any of the parts may be absent, but at least one must be present (V6). |
| An expression | Content, written through the target's content rule (escaped) |
| `if`, `for`, `switch` | Ordinary Bayān control flow |
| `let` | A local binding for the rest of the block |

Element names resolve only within the target's vocabulary and the program's `@element` declarations, with no overload inference. A qualified name (`ReviewForm.fields`) resolves to an `@element` member.

## 3. Lowering

An element becomes a call. Its children block becomes a borrowed (non-escaping) closure, so nothing allocates. An HTML target streams to a writer with no tree in memory; `kit.ui` builds its node table from the same calls. `bayan expand` shows the lowering. Markup needs no files in `gen/`: it lowers in place.

## 4. Targets

| Library | Target | The same markup produces |
| --- | --- | --- |
| `kit.web` | `HtmlWriter` | Streamed HTML, including HTMX attributes (`hxPost:`, `hxTarget:`, `hxSwap:`) |
| `kit.ui` | Node table | Native controls |
| `kit.mail` | `MailWriter` | Email-safe HTML plus a plain-text alternative |
| `kit.term` | Terminal buffer | Terminal UI |
| `kit.svg`, `kit.pdf` | Their writers | Charts and reports |

## 5. Security

| Rule | Example |
| --- | --- |
| Content lines are escaped for their context | `p { review.body }` writes `&lt;script&gt;…` |
| `Untrusted<string>` is safe as content | `p { comment.body }` |
| Attributes take types, not strings | `href` takes `Url`; `Url.tryParse` rejects `javascript:` |
| Raw output needs a reason | `Html.trusted(post.renderedMarkdown) because "sanitized by kit.markdown when saved"` |

## 6. Other forms, same rules

| Form | Use for | Rule |
| --- | --- | --- |
| Tagged literals (`sql"…"`, later others) | Foreign text formats | One general mechanism with typed holes, validated at compile time once the v0.2 interpreter exists |
| Template files via generators | Content written by non-programmers | Generators emit `@element` components into `gen/` through the typed syntax API |
| `bayan import html` | Existing HTML | One-way conversion to markup |

## 7. End-to-end example: product reviews

Show a product with its reviews; post a review through an HTMX form.

### 7.1 Layout

```
reviews/
├── package.byn
├── src/
│   ├── main.byn
│   ├── model/
│   │   ├── review.byn
│   │   └── review-form.byn
│   ├── data/
│   │   └── store.byn
│   └── web/
│       ├── api.byn
│       ├── layout.byn
│       ├── product-page.byn
│       └── review-section.byn
└── gen/                          // written by the build, not committed
```

### 7.2 `package.byn`

```cs
Package {
    name: "local/reviews",
    version: "0.1.0",
    edition: 2026,
    kind: .app,
    uses: [.net, .env],
    dependencies: [
        "kit.web":   Dependency { version: "0.1", uses: [.net] },
        "kit.db.pg": Dependency { version: "0.1", uses: [.net] },
    ],
    db: Database { connections: 32 },     // total across all workers
    style: Style { booleans: .words },
}
```

### 7.3 Model

```cs
// src/model/review.byn
type distinct ProductId = i64
type distinct ReviewId = i64

@status(404)
union error NotFound {
    product(ProductId id)
}

@derive(Row)
struct Product {
    ProductId id
    string name
    Money price
}

@derive(Row)
struct Review {
    ReviewId id
    ProductId productId
    string author
    string body
    i32 stars
    Instant createdAt
}
```

```cs
// src/model/review-form.byn
@derive(Form)
struct ReviewForm {
    string author requires it.length >= 1 and it.length <= 40
    string body requires it.length >= 10 and it.length <= 2000
    i32 stars requires it >= 1 and it <= 5
}
```

### 7.4 Data access

Each function acquires its own connection, so two reads can run at the same time. Queries name their columns in `Row` order; the build checks them against the schema snapshot.

```cs
// src/data/store.byn
Product loadProduct(ProductId id, from Pool pool) uses net, suspend, alloc fails NotFound, DbError {
    var conn = try pool.acquire()
    return try conn.query<Product>(sql"SELECT id, name, price FROM products WHERE id = $1", id).tryFirst()
        ?? fail .product(id)
}

List<Review> loadReviews(ProductId id, from Pool pool) uses net, suspend, alloc fails DbError {
    var conn = try pool.acquire()
    return try conn.query<Review>(
        sql"SELECT id, product_id, author, body, stars, created_at FROM reviews WHERE product_id = $1 ORDER BY created_at DESC LIMIT 50",
        id).all()
}

Review addReview(ReviewForm form, for ProductId product, to Pool pool) uses net, suspend, alloc fails DbError {
    var conn = try pool.acquire()
    return try conn.query<Review>(
        sql"INSERT INTO reviews (product_id, author, body, stars) VALUES ($1, $2, $3, $4) RETURNING id, product_id, author, body, stars, created_at",
        product, form.author, form.body, form.stars).single()
}
```

### 7.5 Views

```cs
// src/web/layout.byn
@element
struct Layout {
    string heading

    void render(ref HtmlWriter writer, by Children<HtmlWriter> content) {
        markup writer {
            html lang: "en" {
                head {
                    title { heading }
                    script src: Url.path("/htmx.min.js")
                }
                body {
                    main { content }
                }
            }
        }
    }
}
```

```cs
// src/web/review-section.byn
@element
struct ReviewSection {
    ProductId product
    List<Review> reviews
    Violations<ReviewForm> errors = []

    void render(ref HtmlWriter writer) {
        markup writer {
            section id: "reviews" {
                form hxPost: ReviewApi.urlForAdd(product) hxTarget: "#reviews" hxSwap: .outerHtml {
                    ReviewForm.fields errors: errors
                    button type: .submit { "Post review" }
                }
                for review in reviews {
                    article class: "review" {
                        p class: "stars" { review.stars }
                        p { review.body }
                        footer { review.author }
                    }
                }
            }
        }
    }
}
```

```cs
// src/web/product-page.byn
@element
struct ProductPage {
    Product product
    List<Review> reviews

    void render(ref HtmlWriter writer) {
        markup writer {
            Layout heading: product.name {
                h1 { product.name }
                p class: "price" { product.price }
                ReviewSection product: product.id reviews: reviews
            }
        }
    }
}
```

### 7.6 Routes

```cs
// src/web/api.byn
@derive(Routes)
struct ReviewApi {
    Pool pool

    @get("/products/{id}")
    public Page show(ProductId id) uses net, suspend, alloc fails NotFound, DbError {
        scope(timeout: 2s) {
            let product = spawn loadProduct(id, from: pool)
            let reviews = spawn loadReviews(id, from: pool)
            return Page(ProductPage { product: try product.join(), reviews: try reviews.join() })
        }
    }

    @post("/products/{id}/reviews")
    public Fragment add(ProductId id, from Untrusted<FormData> data) uses net, suspend, alloc fails DbError {
        let form = ReviewForm.fromForm(data) catch error => {
            let reviews = try loadReviews(id, from: pool)
            return Fragment(ReviewSection { product: id, reviews: reviews, errors: error.violations })
        }
        let _ = try addReview(form, for: id, to: pool)
        let reviews = try loadReviews(id, from: pool)
        return Fragment(ReviewSection { product: id, reviews: reviews })
    }
}
```

`NotFound` becomes a 404 through `@status(404)`. If either load fails or the timeout fires, the scope cancels the other; a connection canceled mid-query is discarded, not returned to the pool.

### 7.7 `main`

```cs
// src/main.byn
void main() uses net, suspend, alloc, env fails NetError, DbError, EnvError {
    let pool = try Pool.open(try Env.read("DATABASE_URL"))
    try Http.listen(at: 8080, workers: .perCore).serve(by: ReviewApi { pool: pool })
}
```

### 7.8 Generated files

```
gen/
├── index.json
└── derive/
    ├── model/
    │   ├── review/
    │   │   ├── Product.row.byn          // readRow, columns, bindInto
    │   │   └── Review.row.byn
    │   └── review-form/
    │       └── ReviewForm.form.byn      // fromForm, fields (an @element)
    └── web/
        └── api/
            └── ReviewApi.routes.byn     // dispatch, urlForAdd
```

`bayan expand ReviewSection` shows the markup lowering:

```cs
writer.open("article")
writer.attribute("class", "review")
writer.endOpen()
writer.open("p")
writer.attribute("class", "stars")
writer.endOpen()
writer.text(review.stars)
writer.close("p")
writer.open("p")
writer.endOpen()
writer.text(review.body)               // escapes <, >, &, quotes
writer.close("p")
…
```

### 7.9 Running it

```
$ bayan run
  derive   Row ×2, Form ×1, Routes ×1   → gen/derive (4 files)
  build    local/reviews  debug  x86_64-linux
  listen   :8080  8 workers  32 connections (4 per worker)
```

- `GET /products/7` loads the product and its reviews at the same time and streams the page.
- A too-short review returns the section with the error beside the field; the input carries `minlength="10"` from the same rule.
- A body of `<script>alert(1)</script>` is stored as typed and rendered as `&lt;script&gt;…`.

### 7.10 What the checks catch

```
a href: review.author { "profile" }
error: `href` takes `Url`, not `string`
  help: parse it with `Url.tryParse(review.author)`, which rejects `javascript:` URLs

spawn loadReviews(id, from: pool)        // outside any scope
error: `spawn` must be inside a `scope`

after updating kit.web to 0.1.4, which reads environment variables:
error: `kit.web` 0.1.4 uses `.env`, which this package doesn't grant it
  help: review the change, then add `.env` to its `uses`, or stay on 0.1.3
```

## 8. Risks

| Risk | Remedy |
| --- | --- |
| Inner blocks calling elements of an outer target (Kotlin's `@DslMarker` problem) | Each block is bound to exactly one target |
| Invalid nesting (`li` outside a list) | Optional child-type constraints per element, later |
| Designers want plain HTML | `bayan import html` |
| Large pages compiling slowly (Swift result builders) | Lowering is syntactic with no inference; cost grows linearly |
| The design is untested | Build Sūq's storefront and one `kit.ui` screen with it before freezing details |

## 9. Open items

- [ ] Child-type constraints: worth it, or leave to documentation?
- [ ] Attribute names that are not identifiers (`data-*`, `aria-*`): typed maps (`data: ["order-id": order.id]`) or a name-mapping rule?
- [ ] Hot reload for views during development: the v0.2 interpreter or TinyCC?
