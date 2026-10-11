# AI Traceability Log

Records how AI was used on each task and what the engineer accepted, edited or rejected, with
the reason and how it was verified.

**Decision values**

- **Accepted after review:** the AI draft was read critically, understood, and verified by a
  test or tool. Nothing needed changing.
- **Edited:** the draft was changed because a tool, test or manual check showed a problem.
- **Rejected / replaced:** the approach was dropped in favour of another.

Entries marked **Real event** are cases where a tool, test or manual check showed the first
result was wrong or incomplete, and the engineer corrected it. These are the strongest evidence
of ownership.

| # | Task | Prompt summary | AI output | Decision | Rationale and verification |
|---|------|----------------|-----------|----------|----------------------------|
| 1 | Entities and DbContext | "Entities for short URL and click event, EF Core, SQLite, unique code" | UTC `DateTime` entities, unique index on `Code`, max lengths | Accepted after review | UTC `DateTime` because SQLite cannot order or group `DateTimeOffset`. Max lengths mirror the validator. Covered by integration tests |
| 2 | Package install | "Install EF Core Sqlite, Design, Swashbuckle, Mvc.Testing" | Unpinned `Install-Package` commands | Edited | **Real event.** The unpinned install pulled EF Core 10.0.12, failing with NU1202 on net8.0. Pinned to 8.0.x |
| 3 | Code generator | "7-char base62 code, unpredictable, thread-safe" | `RandomNumberGenerator.GetInt32` | Accepted after review | Cryptographically secure, no shared state. `System.Random` was considered and rejected as predictable. Unit tests check length, alphabet and uniqueness |
| 4 | Validator | "Validate http/https URLs and aliases, reject javascript:/file:" | Scheme allowlist, regex alias, reserved words | Accepted after review | An allowlist is safer than a blocklist. Unit tests cover dangerous schemes, over-long URLs and reserved aliases |
| 5 | UrlService | "Create short URL, collision-safe, testable time" | Unique-index insert with retry, injected `TimeProvider` | Accepted after review, limitation documented | Avoids the check-then-insert race. The broad `DbUpdateException` catch is a known simplification (`risks.md`) |
| 6 | Service tests | "Unit tests for UrlService" | Tests on SQLite in-memory | Accepted after review | EF's InMemory provider does not enforce unique indexes, which would make the conflict test meaningless |
| 7 | Endpoints | "Minimal API: create, redirect, health" | Endpoints with separate contract types, explicit 302 | Accepted after review | Contracts are decoupled from the DB entity. 302 keeps analytics accurate. Verified through Swagger, curl and integration tests |
| 8 | Error handling | Manual test with a malformed body | Draft had no handling for malformed JSON | Edited | **Real event.** Sending `{hello}` returned a full stack trace in Development. Added a global exception handler. Later, the Docker run showed that in Production the framework returns a bare 400 itself, so the docs were corrected to say so |
| 9 | Integration tests | "End-to-end tests for create, redirect, errors, expiry" | `WebApplicationFactory` tests with a temp DB and fake clock | Accepted after review | `Location` is compared as a `Uri` because .NET normalizes it. A fake clock lets expiry be tested without sleeping |
| 10 | Click recording (brownfield) | "Record a click on each redirect" | Baseline wrote the click synchronously inside the redirect | Replaced | A test that drops the analytics table expects 302, which a synchronous write cannot guarantee. Replaced by an async pipeline (entry 11). *Add the actual failing-run result here only if you ran the baseline* |
| 11 | Async pipeline | "Move click recording off the request path, bounded, failure-tolerant" | Bounded `Channel` and a `BackgroundService` | Accepted after review | Try/catch per click so one bad write cannot stop the worker, and a DI scope per click because `AppDb` is scoped. Analytics tests poll because recording is asynchronous |
| 12 | Header capture | "Store referrer and user-agent" | Capture with `Truncate` and empty-to-null | Accepted after review | Visitor-controlled headers could otherwise bloat the database |
| 13 | Analytics tests | "Test counting with async recording" | Tests that poll with a timeout | Accepted after review | Asserting immediately would be flaky because clicks are recorded in the background |
| 14 | Redirect cache | "Cache redirect lookups with IMemoryCache" | Size-limited cache, misses not cached, expiry re-checked on every hit | Accepted after review | Prevents cache flooding by guessed codes, and keeps expiry accurate for cached links. A test warms the cache, deletes the link and expects 404 |
| 15 | Delete | "Delete a short URL" | Soft delete with cache invalidation | Accepted after review | Keeps analytics history. The cache entry is removed so a deleted link is not served from cache |
| 16 | Rate limiting | "Per-IP rate limit, 429 with Retry-After" | Config-driven fixed window, `Retry-After` from the limiter, `/health` exempt | Accepted after review | Config lets tests use a tiny limit. `QueueLimit = 0` rejects instead of queuing. Limits documented in `risks.md` |
| 17 | Quality gates | "Set up format, analyzers, vuln scan, coverage for .NET 8" | Gate commands and csproj settings | Edited after tool findings | **Real event.** A first build was clean, but the analyzer settings were not in the csproj, so the recommended rules were off. After enabling them there were 4 findings. Fixed CA1725, CA1848 and CA1305. Suppressed CA1711 on `ClickQueue` with a written justification |
| 18 | Dependency scan | "Run vulnerability scan, triage results" | Fix plan: pin newer SQLite native library and patched legacy packages | Accepted after verifying | **Real event.** The scan found 4 high-severity transitive findings. Sources disagreed on whether a patched SQLitePCLRaw 2.x existed, so a re-scan decided it. Pinning `SQLitePCLRaw.lib.e_sqlite3` 2.1.12, `System.Net.Http` 4.3.4 and `System.Text.RegularExpressions` 4.3.1 cleared it, with no suppression needed |
| 19 | Repo layout | "Generate coverage report, fix paths" | Report command with a relative path to the sibling test folder | Edited after checking git | **Real event.** The path problem exposed that the tests were outside the git root and had never been committed. The repo root was moved above both projects. The move dropped the test project's `ProjectReference` (15 compile errors), found with `dotnet list reference` and fixed. History was not rewritten |
| 20 | Ambiguity analysis | "List ambiguities in 'make links expire and make the service more reliable'" | Table of questions with options | Accepted after review | The engineer chose the assumptions and cut click-limit expiry, because asynchronous click counting makes a limit of N inexact |
| 21 | Default expiry | "Configurable default expiry for new links" | Options class, default applied after validation, off unless configured | Accepted after review | Applied after validation so a default cannot be rejected, values of zero or less are ignored, and it is off by default and applies to new links only. The constructor change required updating the existing unit tests |
| 22 | Readiness probe | "Add a readiness endpoint" | Real database query, generated log method | Accepted after review | A real query is stronger than a connection check for SQLite. A test drops the table and expects 503. A generated log method is needed because the analyzers reject direct `LogWarning` calls (CA1848) |
| 23 | Docker smoke test | "Verify the container with curl" | Create call from PowerShell returned an empty response | Investigated, docs corrected | **Real event.** PowerShell mangled the JSON quotes, so the app correctly returned 400. In Production the framework returns a bare 400 with an empty body for unreadable JSON, so the handler's JSON body only applies in Development |

## Secure AI usage

- No secrets, tokens, credentials or real customer data were included in any prompt.
- All generated code was reviewed before commit and run through format, analyzer, test and
  vulnerability gates.
- High-impact changes had explicit human sign-off: the default expiry stays off unless a person
  enables it (`docs/scenarios.md`).
- The engineer owns correctness, maintainability and production readiness of every change.