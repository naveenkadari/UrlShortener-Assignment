# Quality Gates

A quality gate is an automatic check that must pass before code counts as finished.
This file records the **actual results** from the developer machine. Entries marked
*Pending* have not been run yet and must be filled in with real output, not placeholders.

- **Solution:** `UrlShortener.sln` (repository root, containing `UrlShortener/` and `UrlShortener.Tests/`)
- **Target framework:** .NET 8 (LTS)
- **Last updated:** 2026-10-11

## Summary

| # | Gate | Command | Status | Result |
|---|------|---------|--------|--------|
| 1 | Formatting | `dotnet format UrlShortener.sln --verify-no-changes` | Passed | No changes needed |
| 2 | Static analysis | `dotnet build UrlShortener.sln -warnaserror` | Passed | 4 findings: 3 fixed, 1 suppressed |
| 3 | Tests | `dotnet test UrlShortener.sln` | Passed (baseline) | 50 passed, 0 failed, 0 skipped |
| 4 | Dependency vulnerabilities | `dotnet list UrlShortener.sln package --vulnerable --include-transitive` | Passed | 4 high-severity transitive findings fixed by pinning patched versions. Re-scan: no vulnerable packages |
| 5 | Code coverage | `dotnet test --collect:"XPlat Code Coverage"` + ReportGenerator | Passed (measured) | 92.8% line, 78.7% branch |

## Gate 1: Formatting

- **Command:** `dotnet format UrlShortener.sln --verify-no-changes`
- **Result:** Passed. The command printed nothing and made no changes, so the code already
  matched the configured style rules.
- **Note:** The solution file must be passed explicitly. The project folder contains both
  `UrlShortener.csproj` and `UrlShortener.sln`, so running `dotnet format` with no argument
  fails with "Both a MSBuild project file and solution file found". The same applies to
  `dotnet build` and `dotnet test`, so all gate commands use `UrlShortener.sln`.

## Gate 2: Static analysis (warnings as errors)

- **Command:** `dotnet build UrlShortener.sln -warnaserror`
- **Settings in `UrlShortener.csproj`:** `TreatWarningsAsErrors=true`,
  `AnalysisLevel=latest-recommended`, `EnforceCodeStyleInBuild=true`
- **History:** A first build passed, but the settings were not yet in the csproj, so the
  recommended rules were off. After enabling them, the build reported 4 errors.
- **Result:** Passed. 4 findings: 3 fixed, 1 suppressed. Build succeeded, 0 warnings, 0 errors.
- **Findings fixed (3):**

| Rule | File | Fix |
|------|------|-----|
| CA1725 | `Data/AppDb.cs` | Renamed parameter `b` to `modelBuilder` to match the base class |
| CA1848 | `Analytics/ClickRecorder.cs` | Replaced `logger.LogError(...)` with a source-generated `LoggerMessage` method |
| CA1305 | `Program.cs` | Formatted the `Retry-After` value with `CultureInfo.InvariantCulture` |

- **Finding suppressed (1):**

| Rule | Scope | Justification |
|------|-------|---------------|
| CA1711 | `ClickQueue` type only | Thin wrapper over a bounded `Channel`. The name describes its role and is used across the docs and tests. The suppression is scoped to one type, not the project |

## Gate 3: Tests

- **Command:** `dotnet test UrlShortener.sln`
- **Result:** 50 passed, 0 failed, 0 skipped.
- **History:** The baseline was 50 before the analyzer changes. After the analyzer fixes and the
  dependency upgrades the count is still 50 passed, 0 failed.

## Gate 4: Dependency vulnerabilities

- **Commands:**
  - `dotnet list UrlShortener.sln package --vulnerable --include-transitive`
  - `dotnet list UrlShortener.sln package --deprecated`
- **Scan date:** 2026-10-11
- **Result:** Findings reported. No direct package is vulnerable; all findings are transitive.

### Vulnerable packages (initial scan)

| Project | Package | Resolved | Severity | Advisory | Pulled in by |
|---------|---------|----------|----------|----------|--------------|
| UrlShortener | `SQLitePCLRaw.lib.e_sqlite3` | 2.1.6 | High | GHSA-2m69-gcr7-jv3q | `Microsoft.EntityFrameworkCore.Sqlite` (native SQLite binary) |
| UrlShortener.Tests | `SQLitePCLRaw.lib.e_sqlite3` | 2.1.6 | High | GHSA-2m69-gcr7-jv3q | Project reference to the API |
| UrlShortener.Tests | `System.Net.Http` | 4.3.0 | High | GHSA-7jgj-8wvc-jh57 | Legacy transitive dependency of the test tooling |
| UrlShortener.Tests | `System.Text.RegularExpressions` | 4.3.0 | High | GHSA-cmhx-cq75-c4mj | Legacy transitive dependency of the test tooling |

### Deprecated packages

| Project | Package | Version | Reason | Suggested alternative |
|---------|---------|---------|--------|-----------------------|
| UrlShortener | none | | | |
| UrlShortener.Tests | `xunit` | 2.5.3 | Legacy | `xunit.v3` |

### Decisions

| Finding | Decision | Reasoning |
|---------|----------|-----------|
| SQLite native library (production code) | Try a newer `SQLitePCLRaw.lib.e_sqlite3` pinned directly. If the advisory remains, suppress with a written reason | Reports differ on whether a patched 2.x build exists, so the re-scan is the evidence. Do not jump to a 3.x major version, which Microsoft.Data.Sqlite 8 does not bind to. Exposure is limited: the app runs only EF-generated, parameterized queries against its own local file, and never executes caller-supplied SQL |
| `System.Net.Http`, `System.Text.RegularExpressions` (tests only) | Pin direct references to the patched 4.3.x releases | Test-only dependencies that are not shipped in the Docker image. Pinning is a low-risk fix |
| `xunit` 2.5.3 deprecated | Accept and document | Test-only. Migrating to xunit v3 is a framework migration with no security benefit for this assignment |

### Remediation result

Fixes applied and re-verified on 2026-10-11.

| Project | Package | Old | New | Reason |
|---------|---------|-----|-----|--------|
| UrlShortener | `SQLitePCLRaw.lib.e_sqlite3` (direct reference added) | 2.1.6 (transitive) | 2.1.12 | Replaces the vulnerable native SQLite binary |
| UrlShortener.Tests | `System.Net.Http` (direct reference added) | 4.3.0 (transitive) | 4.3.4 | Patched release |
| UrlShortener.Tests | `System.Text.RegularExpressions` (direct reference added) | 4.3.0 (transitive) | 4.3.1 | Patched release |

- **Re-scan** (`dotnet list UrlShortener.sln package --vulnerable --include-transitive`):
  no vulnerable packages in `UrlShortener` or `UrlShortener.Tests`.
- **Suppressions needed:** none. The `NuGetAuditSuppress` fallback was not required.
- **Build:** `dotnet build UrlShortener.sln -warnaserror` succeeded.
- **Tests after changes:** 50 passed, 0 failed, 0 skipped.
- **Accepted and documented:** `xunit` 2.5.3 is flagged deprecated (Legacy). It is test-only and
  not a vulnerability, so the migration to xunit v3 is out of scope.

## Gate 5: Code coverage

- **Commands:**
  - `dotnet test UrlShortener.sln --collect:"XPlat Code Coverage"`
  - `reportgenerator -reports:"UrlShortener.Tests\TestResults\*\coverage.cobertura.xml" -targetdir:coveragereport -reporttypes:"TextSummary;Html"`
  - Run from the repository root. Delete old `TestResults` folders before a run, otherwise
    runs are merged.
- **Measured on:** 2026-10-11, single clean run (50 tests). Measured before the repository
  restructure described below. Code is unchanged, so the figures still apply.

| Metric | Value |
|--------|-------|
| Line coverage | 92.8% (400 of 431 coverable lines) |
| Branch coverage | 78.7% (85 of 108 branches) |
| Method coverage | 92.4% (49 of 53) |
| Fully covered methods | 79.2% (42 of 53) |

- **Per class:**

| Class | Line coverage |
|-------|---------------|
| `Program` (endpoints and wiring) | 97.8% |
| `UrlValidator` | 100% |
| `CodeGenerator` | 100% |
| `AppDb` | 100% |
| `ClickQueue` | 100% |
| `UrlService` | 90.6% |
| `ClickRecorder` | 60% |
| `ShortUrl` | 83.3% |
| `ClickEvent` | 80% |
| Generated alias regex | 90.7% |

- **Interpretation:** Coverage is used as a guide to find untested logic and failure paths,
  not as a target to maximise. Core logic (`UrlValidator`, `CodeGenerator`, `AppDb`) is fully
  covered, and endpoints are covered by integration tests.
- **Known gaps:**
  - `ClickRecorder` (60%): the background worker's failure path (logging a failed click) is
    not exercised deterministically. The existing test that breaks the analytics table
    proves the redirect survives, but does not wait for the worker to process the failure.
  - `UrlService` (90.6%): *(fill in from `coveragereport/index.html`: which lines are red)*
  - `ShortUrl` and `ClickEvent` are property-only classes. Unhit lines are unused
    property accessors and not a real gap.

## Repository hygiene

- `urls.db`, `urls.db-shm` and `urls.db-wal` were previously committed by mistake. They have
  been removed from git tracking (`git rm --cached`) and `urls.db*` added to `.gitignore`,
  along with `TestResults/` and `coveragereport/`.
- **Finding: the test project was outside version control.** While locating coverage files it
  became clear that the git root was the API folder, and `UrlShortener.Tests` sat in a sibling
  folder that had never been committed. Fix: the repository root was moved above both projects
  and a single `UrlShortener.sln` created at that level. Commit `267b24a` ("Restructure: repo
  root above both projects so tests are tracked") added the tests to git for the first time and
  recorded the API files as 100% renames. Earlier commits and any tags therefore contain only
  the API project. History was not rewritten.
- **Verification after the restructure:** the test project had lost its `ProjectReference` to
  the API during the move, producing 15 compile errors. Re-adding the reference restored a clean
  `dotnet build UrlShortener.sln -warnaserror` and 50 passing tests.
- No secrets, tokens, or real customer data are used by this application or were included in
  AI prompts. The only configuration value is a local SQLite file name.
