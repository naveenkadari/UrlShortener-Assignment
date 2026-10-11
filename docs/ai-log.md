# AI Traceability Log

Records how AI was used on each task, and what the engineer accepted, edited or rejected.
Rule: log what actually happened. Entries marked **verify** describe a typical first draft
and must be reworded if the behavior was not actually observed in the engineer's own session.

Decision values: **Accepted**, **Edited**, **Rejected**.

| # | Task | Prompt summary | AI output | Decision | Rationale |
|---|------|----------------|-----------|----------|-----------|
| 1 | Entities + DbContext | "Entities for short URL and click event, EF Core, SQLite, unique code" | Used `DateTimeOffset`, no max lengths (**verify**) | Edited | Switched to UTC `DateTime` for SQLite ordering and grouping. Added max lengths so the schema matches validation |
| 2 | Package install | "Install EF Core Sqlite, Design, Swashbuckle, Mvc.Testing" | Unpinned `Install-Package` commands | Edited | **Real event.** Unpinned install pulled EF Core 10.0.12, failing with NU1202 on net8.0. Pinned to 8.0.x |
| 3 | Code generator | "7-char base62 code, unpredictable, thread-safe" | `RandomNumberGenerator.GetInt32` | Accepted | Cryptographically secure, no shared mutable state. `System.Random` was considered as a design alternative and rejected because it is predictable |
| 4 | Validator | "Validate http/https URLs and aliases, reject javascript:/file:" | Scheme allowlist, regex alias, reserved words | Accepted, plus tests | Allowlist is safer than a blocklist. Added the case-insensitive `API` test and the error-message test |
| 5 | UrlService | "Create short URL, collision-safe, testable time" | Unique-index insert with retry, injected `TimeProvider` | Accepted, limitation documented | Avoids check-then-insert race. Broad `DbUpdateException` catch is a known simplification |
| 6 | Service tests | "Unit tests for UrlService" | Tests using SQLite in-memory | Accepted | EF InMemory does not enforce unique indexes, which would make the conflict test meaningless |
| 7 | Endpoints | "Minimal API: create, redirect, health" | Request/response types inline in `Program.cs` (**verify**) | Edited | Moved to `Contracts/` so the public API is decoupled from the DB entity. Redirect is explicitly 302 for analytics |
| 8 | Error handling | "Endpoints: create, redirect, health" | No handling for malformed JSON | Edited | **Real event.** Sending `{hello}` showed a full stack trace. Added a global exception handler returning clean 400/500 JSON |
| 9 | Integration tests | "End-to-end tests for create, redirect, errors, expiry" | `Location` compared as a string (**verify**) | Edited | .NET normalizes `Location` to a `Uri`, so compare as `Uri`. DB moved to temp folder with cleanup. `FakeTimeProvider` used for expiry |
| 10 | Click recording | "Record a click on each redirect" | Synchronous `SaveChanges` inside the redirect | Accepted as baseline, then rejected | **Real event.** A failing test proved the redirect returned 500 when the analytics table was unavailable. Analytics must not block the primary flow |
| 11 | Async pipeline | "Move click recording off the request path, bounded, failure-tolerant" | `Channel` + `BackgroundService` | Accepted, with edits | Added try/catch inside the loop and a scope per click, because the scoped `AppDb` cannot be held by a singleton |
| 12 | Header capture | "Store referrer and user-agent" | Raw header values (**verify**) | Edited | Added `Truncate` and empty-to-null, because visitor-controlled headers could bloat the database |
| 13 | Analytics tests | "Test counting with async recording" | Assert immediately after redirect (**verify**) | Edited | Recording is async, so tests poll with a 5-second timeout instead of asserting immediately |
| 14 | Redirect cache | "Cache redirect lookups with IMemoryCache" | Unbounded cache, cached misses, expiry checked only at fill (**verify**) | Edited | Added size limit, no caching of misses, and expiry re-checked on every hit |
| 15 | Delete | "Delete a short URL" | Hard delete of the row (**verify**) | Edited to soft delete | Hard delete would orphan analytics. Added cache invalidation, with a test that fails without the `Remove` line |
| 16 | Rate limiting | "Per-IP rate limit, 429 with Retry-After" | Hardcoded limit, hardcoded `Retry-After`, `/health` throttled (**verify**) | Edited | Limit made configurable so tests do not flake. `Retry-After` computed from the limiter. `/health` exempt. `QueueLimit = 0` |
| 17 | Quality gates | "Set up format, analyzers, vuln scan, coverage for .NET 8" | Gate commands and csproj settings; existing code had `logger.LogError`, `int.ToString()`, and a short override parameter name | Edited after tool findings | **Real event.** A first build was clean but the analyzer settings were not in the csproj, so the recommended rules were off. After enabling them: 4 findings. Fixed CA1725, CA1848, CA1305. Suppressed CA1711 on `ClickQueue` with a written justification. Details in `quality-gates.md` |
| 18 | Dependency scan | "Run vulnerability scan, triage results" | Fix plan: pin newer SQLite native library and patched legacy packages | Accepted after verifying | **Real event.** Scan found 4 high-severity transitive findings. Sources disagreed on whether a patched SQLitePCLRaw 2.x existed, so I did not trust either claim and re-scanned. Pinning `SQLitePCLRaw.lib.e_sqlite3` 2.1.12, `System.Net.Http` 4.3.4 and `System.Text.RegularExpressions` 4.3.1 cleared the scan, and 50 tests still pass. No suppression needed. `xunit` deprecation accepted and documented |
| 19 | Repo layout and coverage paths | "Generate coverage report, fix paths" | Report command with a relative path to the sibling test folder | Edited after checking `git rev-parse --show-toplevel` | **Real event.** The path problem exposed that the tests were outside the git root and had never been committed. Moved the repo root above both projects, recreated the solution, committed (`267b24a`). The move dropped the test project's `ProjectReference` (15 compile errors), found and fixed with `dotnet list reference`. 50 tests pass again. History not rewritten |

## Secure AI usage

- No secrets, tokens, credentials or real customer data were included in any prompt.
- All generated code was reviewed before commit and run through format, analyzer, test and
  vulnerability gates.
- The engineer owns correctness, maintainability and production readiness of every change.
