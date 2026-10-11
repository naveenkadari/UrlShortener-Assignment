$ErrorActionPreference = "Stop"

Write-Host "1/4 Format check"
dotnet format UrlShortener.sln --verify-no-changes
if ($LASTEXITCODE -ne 0) { throw "Format check failed" }

Write-Host "2/4 Build (warnings as errors)"
dotnet build UrlShortener.sln -warnaserror
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

Write-Host "3/4 Tests with coverage"
Remove-Item -Recurse -Force UrlShortener.Tests\TestResults -ErrorAction SilentlyContinue
dotnet test UrlShortener.sln --no-build --collect:"XPlat Code Coverage"
if ($LASTEXITCODE -ne 0) { throw "Tests failed" }

Write-Host "4/4 Vulnerable packages"
dotnet list UrlShortener.sln package --vulnerable --include-transitive

Write-Host "Quality gates finished. Read the output of step 4."