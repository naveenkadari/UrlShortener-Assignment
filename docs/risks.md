# Risks, Trade-offs and Known Gaps

| Risk | Mitigation in place | Remaining gap |
|------|--------------------|---------------|
| Short code collision | Unique index, up to 5 retries, concurrent-create test (50 requests) | The save catch treats any `DbUpdateException` as a collision. Production should inspect the inner exception |
| Phishing or abuse via valid URLs | http/https allowlist, 2048 length cap | No reputation or blocklist check |
| Dangerous schemes (`javascript:`, `file:`) | Allowlist rejects them | None known |
| Code enumeration | Random codes, per-IP rate limit | IP limits are bypassable. No per-user keys |
| Analytics loss | Bounded queue, failures logged and skipped | Queue is not drained on shutdown. Clicks are dropped when full. Counts are eventually consistent |
| Redirect depends on analytics | Clicks are queued, never written inline | None. Proven by a test that drops the analytics table |
| Stale cache after delete | Cache entry removed on delete, 5 minute TTL, test | In-memory per process. Multiple instances need a shared cache |
| Expired link served from cache | Expiry re-checked on every hit | None known |
| Rate limiter limits | Per-IP, config-driven, `/health` exempt, `QueueLimit = 0` | Per process. Behind a proxy all users share one IP unless forwarded headers are configured. Redirects share the create limit |
| SQLite single writer | Acceptable for a prototype | Concurrent write load needs PostgreSQL or SQL Server |
| `EnsureCreated` at startup | Prototype shortcut | Use EF migrations in production |
| Deleted alias cannot be reused | Documented | Needs a retention policy |
| Default expiry surprises users | Off by default, applies only to new links, invalid values ignored, needs sign-off | None |
| Readiness probe depth | Runs a real query | Does not check disk space or the background worker |
| Error responses | Global handler returns generic 400/500 JSON, no stack traces | Rate-limit bodies are generic by design |
| Vulnerable dependencies | Scanned transitively, three packages pinned (see quality gates) | `xunit` 2.5.3 is flagged deprecated (test only) |
| Performance | Cache, async clicks, aggregation in SQL | No load test was run. Performance is reasoned about, not measured |
| Container build | Multi-stage, non-root user, named volume | Docker build was not verified by the author at the time of writing. Verify with `docker compose up --build` |
| AI-specific | All output reviewed, tests and gates, no secrets in prompts | Human review is the control |
| Click-limit expiry | Deliberately not built | Async counting makes a limit of N inexact. Needs its own design |

## Process finding

The test project sat outside the git root and was never committed until the repository was
restructured. Earlier commits therefore contain only the API project. See
`docs/quality-gates.md`.
