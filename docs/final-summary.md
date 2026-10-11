# Final Engineering Summary

## 1. Plan and rationale

Built bottom-up so every step compiled and had passing tests: domain and data, code generator,
validator, service, endpoints, integration tests, click analytics, delete and caching, rate
limiting, quality gates, then the ambiguous-requirement scenario and packaging. Each task was
defined with intent, constraints and acceptance criteria, then implemented with AI assistance,
reviewed, tested and logged. The engineer owns correctness, maintainability and production
readiness of every change.

## 2. Artifacts

| Artifact | Location |
|----------|----------|
| Working prototype | `UrlShortener/` (run with Docker or `dotnet run`) |
| Tests | `UrlShortener.Tests/` (unit and integration), 56 expected passing |
| API definition | Swagger at `/swagger`. Export `/swagger/v1/swagger.json` to `docs/openapi.json` |
| Setup instructions | `README.md` |
| Architecture | `docs/architecture.md` |
| Three scenarios | `docs/scenarios.md` |
| Risks and trade-offs | `docs/risks.md` |
| Quality gate results | `docs/quality-gates.md`, `quality.ps1` |
| AI traceability | `docs/ai-log.md` |
| Packaging | `Dockerfile`, `docker-compose.yml` |

## 3. Risks, trade-offs and validation

See `docs/risks.md` and `docs/quality-gates.md`.

- Format check: clean.
- Analyzers as errors: 4 findings, 3 fixed, 1 suppressed with a written reason.
- Dependency scan: 4 transitive findings fixed by pinning patched versions, re-scan clean.
- Coverage: 92.8% line, 78.7% branch, measured on the 50-test suite *(re-measure with
  `./quality.ps1` after Scenario 3 and update this line)*.
- Key trade-offs: 302 over 301, async click recording over perfect counts, SQLite over a server
  database, soft delete over hard delete.
- Failure modes tested: duplicate alias, concurrent creates, malformed JSON, expired link,
  analytics failure, deleted-but-cached link, rate limit exceeded, broken database readiness.

## 4. Assumptions

See the Scenario 3 table in `docs/scenarios.md`. In short: time-based expiry only, 410 for
expired links, disable rather than delete, default expiry off unless configured and approved,
and "reliable" means operational readiness on top of the existing safeguards.

## 5. Limitations

- Single instance: the cache and rate limiter are per process.
- SQLite: one writer at a time, `EnsureCreated` instead of migrations.
- No authentication or per-user keys.
- Queue is not drained on shutdown and drops clicks when full.
- No click-limit expiry, no reputation check on target URLs.
- `ClickRecorder` failure path is not deterministically covered by a test (60% line coverage).
- No load test was run, so performance is reasoned about, not measured.
- Deleted aliases cannot be reused.
- Earlier git history excludes the tests (they were untracked until the repository restructure).
