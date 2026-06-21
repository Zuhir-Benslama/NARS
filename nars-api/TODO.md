# nars-api — Code Quality TODO

## Completed

- [x] **IDE0011** — Added braces to all control statements (if/for/foreach/while/lock) across the codebase
- [x] **IDE0007** — Replaced explicit types with `var` where idiomatic (out variables, locals, for-loop counters)
- [x] **IDE0022** — Used expression body for methods (LocationsController, FeatureQueryHelper, SqlFragments, NarsControllerBase, migrations)
- [x] **IDE0300/IDE0028/IDE0305** — Simplified collection initialization (collection expressions, `[]` syntax)
- [x] **IDE0090** — Simplified `new` expression (`new()` instead of `new TypeName`)
- [x] **IDE0042** — Deconstructed variable declarations
- [x] **IDE0078** — Used pattern matching
- [x] **IDE0037** — Simplified member names
- [x] **CA1873** — Guarded expensive logging arguments with `IsEnabled` checks
- [x] **CA2016** — Forwarded `CancellationToken` to async methods
- [x] **CA1861** — Used `static readonly` fields for constant array arguments
- [x] **CA1510** — Used `ArgumentNullException.ThrowIfNull`
- [x] **CA1816** — Added `GC.SuppressFinalize(this)` to `DisposeAsync`
- [x] **CA1860** — Prefer comparing `Count` to 0 over `Any()`
- [x] **IDE0290** — Used primary constructor
- [x] **IDE0330** — Used `System.Threading.Lock`
- [x] **ASP0015** — Used property accessors for response headers
