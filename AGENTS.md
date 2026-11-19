# Claims Platform

## Commands
- Infra only (to `dotnet run` the API): `docker compose up -d postgres kafka`
- Everything incl. the API in Docker (needs `JWT_KEY` in .env; port 5249, so not with `dotnet run`): `docker compose up -d --build`
- Reset DB + Kafka (after any schema or event change; no migrations): `docker compose down -v && docker compose up -d postgres kafka`
- Build: `dotnet build`
- Test: `dotnet test`
- Dev JWT key (once): `dotnet user-secrets set Jwt:Key "<random, 32+ chars>" --project backend/Claims.Api`
- Run API: `dotnet run --project backend/Claims.Api`
- Run web: `cd frontend/claims-web && npm start` (http://localhost:4200, proxies `/api` to the API on :5249)
- Test web: `cd frontend/claims-web && npx ng test --watch=false`

## Rules
- No MediatR / AutoMapper / repository layer; use EF Core directly
- API uses DTOs only, never entities
- Status changes only through Claim methods
- Money = decimal + currency; never add different currencies
- No personal data in logs or Kafka events
- Domain: tests first
- Keep it simple; mark shortcuts: `// shortcut: <limit>, <when to upgrade>`

## Docs
- Status flow and decisions: docs/decisions.md
- Assumptions: docs/assumptions.md
