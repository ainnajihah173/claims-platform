# Claims Platform

## Commands
- Infra: `docker compose up -d`
- Reset DB + Kafka (after any schema or event change; no migrations): `docker compose down -v && docker compose up -d`
- Build: `dotnet build`
- Test: `dotnet test`
- Dev JWT key (once): `dotnet user-secrets set Jwt:Key "<random, 32+ chars>" --project backend/Claims.Api`
- Run API: `dotnet run --project backend/Claims.Api`
- Run web: `cd frontend/claims-web && npm start`

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
