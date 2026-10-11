# Architecture Overview

## Components

```
Client
  |
  v
Rate limiter (per IP, 429 + Retry-After)  -->  Global exception handler (clean 400/500 JSON)
  |
  v
Endpoints (Program.cs)  - HTTP only, no business rules
  |-- POST /api/v1/urls ------> UrlService --> UrlValidator, CodeGenerator --> AppDb (SQLite)
  |-- GET  /{code} -----------> IMemoryCache --miss--> AppDb
  |                               '--> ClickQueue (bounded Channel) --> ClickRecorder
  |                                                                     (BackgroundService) --> AppDb
  |-- GET  /api/v1/urls/{code}/stats --> AppDb (aggregates in SQL)
  |-- DELETE /api/v1/urls/{code} -----> AppDb (soft delete) + cache invalidation
  '-- GET  /health, /health/ready ----> liveness / real database query
```

| Layer | Folder | Responsibility |
|-------|--------|----------------|
| Web | `Program.cs` | Map HTTP to services, translate outcomes to status codes |
| Contracts | `Contracts/` | Public request and response types, decoupled from DB entities |
| Logic | `Services/` | `UrlService`, `UrlValidator`, `CodeGenerator`, `UrlOptions` |
| Analytics | `Analytics/` | `ClickQueue` and `ClickRecorder` background worker |
| Data | `Data/`, `Domain/` | `AppDb` (EF Core), `ShortUrl`, `ClickEvent` |

## Control flow

**Create:** validate URL, validate alias, check expiry is in the future, apply optional default
expiry, pick code (alias or random), insert. A unique-index violation retries with a new random
code (up to 5 times) or returns 409 for a custom alias.

**Redirect:** cache lookup, database on miss (only found rows are cached), 404 if unknown or
deleted, 410 if expired (re-checked on every hit, even when cached), enqueue click without
waiting, return 302.

**Delete:** mark `IsDeleted`, save, remove the cache entry. Rows and click history are kept.

**Analytics write (background):** read from the channel, create a fresh DI scope per click,
save, log and skip on failure so one bad write never stops the worker.

## Key decisions

| Decision | Rejected alternative | Reason |
|----------|----------------------|--------|
| 302 redirect | 301 | 301 is cached by browsers and would hide repeat visits from analytics |
| Unique index plus retry | Check, then insert | Check-then-insert races under concurrency |
| Random base62 codes (`RandomNumberGenerator`) | Sequential IDs, `System.Random` | Sequential codes are enumerable, `Random` is predictable |
| Async clicks via bounded `Channel` | Write inside the redirect | A synchronous write made the redirect fail when analytics failed |
| Drop clicks when queue is full | Block the redirect | Availability over perfect counts |
| Soft delete | Hard delete | Keeps analytics history |
| 410 expired, 404 deleted or unknown | 404 for everything | Distinguishes "existed" from "never existed" |
| Expiry checked on every hit | Check only when cached | A cached link must still expire on time |
| Cache misses are not cached | Cache everything | Guessing random codes would fill the cache |
| Injectable `TimeProvider` | `DateTime.UtcNow` | Expiry tests need no sleeping |
| SQLite | PostgreSQL | Zero setup for a reviewer. Swap the provider for production |
| Per-IP fixed window rate limit, config-driven | Hardcoded limit | Tests and environments can tune it |
| Default expiry off unless configured | On by default | It changes behavior for every future link and needs sign-off |

## Execution approach (AI-assisted)

1. Define the task: intent, constraints, acceptance criteria, technical context.
2. Prompt the AI and review the output critically.
3. Accept, edit or reject, and log the decision with a rationale (`docs/ai-log.md`).
4. Run the quality gates: format, analyzers as errors, tests, vulnerability scan, coverage.
5. Engineer sign-off, then commit in small steps.

Build order was bottom-up (domain, data, generator, validator, service, endpoints, integration
tests, analytics, delete and cache, rate limiting, quality gates, scenario 3), so every step
ended compiling with passing tests.
