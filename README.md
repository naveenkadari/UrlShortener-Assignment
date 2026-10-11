# URL Shortener (.NET 8)

A minimal ASP.NET Core API that shortens URLs, redirects, records click analytics
asynchronously, and protects itself with rate limiting. Built as an AI-assisted engineering
exercise: every AI-generated change was reviewed, tested and logged (see `docs/`).

## Repository layout

```
UrlShortener.sln
UrlShortener/          API project (Minimal API, EF Core + SQLite)
UrlShortener.Tests/    xUnit unit and integration tests
docs/                  architecture, scenarios, risks, quality gates, AI log, final summary
quality.ps1            runs all quality gates
Dockerfile, docker-compose.yml
```

## Run

With Docker (serves on http://localhost:8080):

```
docker compose up --build
```

Without Docker (requires the .NET 8 SDK):

```
dotnet run --project UrlShortener
```

The console prints the URL. Swagger UI is at `/swagger`.

## Test

```
dotnet test UrlShortener.sln
./quality.ps1        # format, warnings-as-errors build, tests + coverage, vulnerability scan
```

## API

| Method | Path | Success | Errors |
|--------|------|---------|--------|
| POST | `/api/v1/urls` | 201 with short code | 400 invalid input, 409 alias taken |
| GET | `/{code}` | 302 with `Location` header | 404 unknown or deleted, 410 expired |
| GET | `/api/v1/urls/{code}/stats` | 200 with click statistics | 404 unknown code |
| DELETE | `/api/v1/urls/{code}` | 204 (soft delete) | 404 unknown or already deleted |
| GET | `/health` | 200 liveness | |
| GET | `/health/ready` | 200 ready | 503 database unusable |

Any endpoint can return 429 (with `Retry-After`) when the per-IP rate limit is exceeded.

### Try it

```
curl -s -X POST http://localhost:8080/api/v1/urls -H "Content-Type: application/json" \
     -d '{"url":"https://www.example.com/a/long/path"}'

curl -i http://localhost:8080/<code>              # 302 + Location
curl -s http://localhost:8080/api/v1/urls/<code>/stats
curl -i -X DELETE http://localhost:8080/api/v1/urls/<code>
```

Optional request fields: `customAlias` (3 to 32 chars: letters, digits, `-`, `_`) and
`expiresAt` (ISO 8601, must be in the future).

On Windows PowerShell use `curl.exe` and escape quotes as `\"`.
Swagger UI cannot follow the redirect (the browser blocks the cross-origin response), so test
`GET /{code}` in a browser or with `curl -i`.

## Configuration

| Key | Default | Meaning |
|-----|---------|---------|
| `ConnectionStrings:Default` | `Data Source=urls.db` | SQLite file |
| `RateLimit:PermitLimit` | 100 | Requests per window, per client IP |
| `RateLimit:WindowSeconds` | 60 | Window length |
| `Links:DefaultExpiryDays` | unset | Default expiry for new links. Off unless set |

In Docker, use environment variables with double underscores, for example
`Links__DefaultExpiryDays=30`.

## Design in one paragraph

Random base62 codes with a database unique index (no check-then-insert race), a 302 redirect so
browsers do not cache it and hide clicks, click events queued in a bounded channel and written by
a background worker so analytics can never break a redirect, soft delete with cache invalidation,
and an injectable clock so expiry is tested without sleeping. Details and rejected alternatives
are in `docs/architecture.md`.

## Documentation

- `docs/architecture.md` components, control flow, key decisions
- `docs/scenarios.md` greenfield, brownfield and ambiguous scenarios
- `docs/risks.md` risks, trade-offs, known gaps
- `docs/quality-gates.md` results of format, analyzers, tests, vulnerability scan, coverage
- `docs/ai-log.md` AI traceability log
- `docs/final-summary.md` plan, artifacts, assumptions, limitations

## Limitations

Single instance, SQLite, no authentication, per-process cache and rate limiter. See
`docs/risks.md`.
