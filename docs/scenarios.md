# Scenarios

Each scenario shows the requirement, ambiguities and assumptions, decomposition, how AI was
used, validation, and the result.

> Evidence note: the test project was not under version control until the repository
> restructure (see `docs/quality-gates.md`). Earlier commits therefore contain only the API
> project, so the evidence for Scenarios 1 and 2 is the recorded test output and this document
> rather than before and after diffs of the tests. History was not rewritten.

## Scenario 1: Greenfield

**Requirement:** "Build a service that shortens URLs and redirects."

**Ambiguities and assumptions**

| Question | Assumption |
|----------|------------|
| Same URL submitted twice? | A new code each time (no de-duplication). Simpler, and avoids leaking whether a URL was shortened before |
| Custom aliases? | Supported, 3 to 32 chars of letters, digits, `-`, `_`. Reserved words (`api`, `health`, `swagger`) rejected |
| Redirect status? | 302, so browsers do not cache it and analytics stay accurate |
| Allowed URLs? | Absolute http and https only, max 2048 characters |

**Decomposition (bottom-up)**

| # | Task | Depends on |
|---|------|------------|
| 1 | Entities and `AppDb` with a unique index on `Code` | none |
| 2 | `CodeGenerator` | none |
| 3 | `UrlValidator` | none |
| 4 | `UrlService.CreateAsync` | 1, 2, 3 |
| 5 | Create, redirect and health endpoints, global error handler | 4 |
| 6 | Unit and integration tests | 1 to 5 |

**AI usage:** generated entities, generator, validator, service, endpoints and tests from
prompts with constraints. Edits made by the engineer: UTC `DateTime` instead of
`DateTimeOffset` (SQLite ordering), secure randomness, max lengths matching validation, a global
exception handler after manual testing showed a stack trace for malformed JSON, `Uri`
comparison for the `Location` header.

**Validation:** 39 tests passing at this stage (unit, integration, concurrent creates, expiry with
a fake clock, malformed JSON). Manual checks through Swagger and curl.

**Result:** working create and redirect with collision-safe code generation.

## Scenario 2: Brownfield

**Requirement:** "Add click analytics to the existing service."

**Codebase reasoning (impacted areas)**

| Area | Impact |
|------|--------|
| Redirect endpoint in `Program.cs` | Records a click on every successful redirect |
| New stats endpoint | Aggregates in SQL (count, last access, per day, top referrers) |
| `ClickEvent`, `AppDb` | Already present, no schema change |
| `Analytics/` | New queue and background worker (second pass) |
| Tests | New analytics tests |

**Execution in two passes**

1. **Pass A (naive):** write the click inside the redirect request. A test was written that
   drops the analytics table and expects the redirect to still return 302. Against Pass A that
   test is expected to fail, because a database problem in the side feature breaks the main flow.
   *(Record here the actual result you saw when running Pass A, for example "43 passed, 1
   failed". Only claim it if you ran it.)*
2. **Pass B (fix):** replace the inline write with a bounded `Channel` and a `BackgroundService`
   that writes clicks with its own DI scope and a try/catch per click. The test was not edited.

**Validation:** 44 tests passing after Pass B. Analytics tests poll for results because clicks are
recorded asynchronously.

**Later changes in the same area:** soft delete with cache invalidation (a test warms the cache,
deletes, and expects 404), a bounded redirect cache that never caches misses, per-IP rate
limiting. Tests after these: 50.

**Trade-offs:** clicks can be lost on a crash or when the queue is full, the queue is not drained
on shutdown, stats are eventually consistent.

**Result:** the redirect no longer depends on analytics.

## Scenario 3: Ambiguous requirement

**Requirement:** "Make links expire and make the service more reliable."

**Ambiguities and assumptions**

| Question | Assumption and why |
|----------|--------------------|
| Expire by time or by clicks? | **Time only.** Already built. A click limit is deferred: clicks are recorded asynchronously, so a limit of N could be exceeded under concurrency, and that needs its own design |
| Who sets the expiry? | **Caller first, optional configured default second.** No default configured means links never expire |
| Delete the data or disable the link? | **Disable only.** Rows and analytics are kept |
| Status for an expired link? | **410 Gone** |
| What does "more reliable" mean? | **Operational readiness:** a probe that reports whether the database is usable. Collision safety, non-blocking analytics and rate limiting already existed |
| Should existing links change? | **No.** The default applies only to links created after it is enabled |

**Human sign-off (high-impact change):** a default expiry silently changes behavior for every
future link, so it ships **off by default** (`Links:DefaultExpiryDays` unset). Enabling it
requires explicit approval.

> Approved to remain disabled by default: Naveen Kadari  Date: 11-10-20226

**Decomposition**

| # | Task | Depends on |
|---|------|------------|
| 1 | `UrlOptions` bound from configuration | none |
| 2 | Apply the default in `UrlService`, after validation | 1 |
| 3 | Unit tests: default applied, explicit expiry wins, no default | 2 |
| 4 | Integration test: default applied, then the link expires | 2 |
| 5 | `/health/ready` endpoint using a real query | none |
| 6 | Readiness tests including a broken database | 5 |
| 7 | Documentation and risk notes | all |

**AI usage:** AI listed the ambiguities. The engineer chose the assumptions and cut click-limit
expiry. Design points the engineer owned: apply the default after validation so it cannot be
rejected, ignore values of zero or less, keep it off by default, and use a real query for the
readiness probe because a plain connection check can pass with broken tables. The analyzers
required a generated log method for the readiness warning.

**Validation:** all tests green (expected 56: 50 existing, 3 unit, 3 integration). The readiness
failure path is proven by dropping the `Urls` table and expecting 503. The expiry path is proven
by advancing a fake clock 8 days.

**Risks:** a default expiry can surprise users (off by default, new links only). Readiness does
not check disk space or the background worker.

**Result:** configurable default expiry and a readiness probe, with every assumption written down.
