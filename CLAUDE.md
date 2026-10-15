# Levo Fitness App

## Conventions

- Nullable reference types on; don't suppress warnings, fix them.
- When generating code, at the end, loop tests and rebuilding the project until there are no errors before returning the code back.
- When implementing any feature, always include full coverage unit and integration tests.
- When modifying existing code, make sure related tests remain functional.
- Codebase follows Vertical Slicing Architecture.
- Perform appropriate commits with detailed summary and description, but dont push anything GitHub.

## Commands (backend, run from `backend/`)

- Format check: `dotnet format --verify-no-changes` (fix: `dotnet format`)
- Lint + type check: `dotnet build` (warnings are errors)
- Tests: `dotnet test ../tests/backend.Tests`
- Design details (auth, sync protocol, resolved decisions): `backend/backend-architecture.md`

## Tech Stack:

- Web: React
- Mobile: React Native Expo
- Mobile Local Storage: SqLite
- Styling / UI: Radix UI
- Backend Runtime: ASP.NET Core (.NET)
- Compute / Hosting: Serverless - API Gateway, AWS Lambda w/ SnapStart
- Database: Supabase
- ORM / Data Layer: Entity Framework Core
- Auth: Supabase Auth
- File / Blob Storage: AWS S3
- Monitoring / APM: AWS CloudWatch
- Transactional Email: Resend
- Payments: Stripe
- AI / LLM: Anthropic Claude
- Search: Postgres FTS
- CI / CD: GitHub Actions

## Sync Layer:

Mobile App → SQLite (local operations during workout)
↓
Sync Queue Manager (custom logic in app)
↓
API Gateway → Lambda Functions (sync orchestration & conflict resolution)
↓
Supabase Database (persistent storage layer)
↑
Supabase Realtime (notifications only)
