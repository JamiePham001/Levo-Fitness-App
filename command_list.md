## Commands (run from `backend/`)

- Run: `dotnet run`
- Format check: `dotnet format --verify-no-changes` (fix: `dotnet format`)
- Lint + type check: `dotnet build` (warnings are errors; nullable and analyzers enforced via `backend.csproj`)
