# Security

## Threats and controls

| Threat | MVP control | Production control |
|---|---|---|
| Fake identity | Signed JWT | OIDC (Entra ID / Keycloak), MFA for staff |
| Changing data you shouldn't | DTOs + domain rules + version check | Same |
| Denying an action | Append-only history | Tamper-evident audit store |
| Seeing others' data | Ownership check, 404; staff see all markets and teams (MVP) | Filter staff by `team` and market, field-level encryption, data residency per market |
| Flooding the API | — | Rate limit / WAF at gateway |
| Gaining staff powers | Role policies, deny by default | Approval limits by amount |

## How it works in the MVP

- **Login.** `POST /api/auth/dev-login` with `{ "userId": "<seeded id>" }` returns a JWT. It only works in Development **and** with `DevLogin__Enabled=true` (set only in `launchSettings.json`, which `dotnet publish` does not ship); otherwise 404. It takes no password (shortcut). The token has `sub` (user id), `role`, `name` and `team` (staff only), and lasts 1 hour (+5 min default clock skew).
- **Validation.** `AddJwtBearer` checks the signature (HMAC-SHA256), issuer, audience and expiry. The signing key comes from user-secrets or the `Jwt__Key` env var and is never in the repo. The API won't start without it, or with one shorter than 32 characters:
  `dotnet user-secrets set Jwt:Key "<random, at least 32 characters>" --project backend/Claims.Api`
- **Deny by default.** The fallback policy makes every endpoint need a valid token. Only dev-login, `/openapi` and `/scalar` allow anonymous access.
- **Roles.** `ClaimantOnly`: submit a claim, answer a question. `StaffOnly` (Officer, Manager): pick up, request info, set reserve, settle, reject. A claimant who calls a staff action gets **403**. Any other or missing role sees no claims.
- **Ownership.** A claimant's queries only include claims where `ClaimantId` = their `sub`, so another claimant's claim returns **404**, the same as one that doesn't exist (no hint that it's there). Staff can read all claims. On every officer action, the `Claim` method checks that the acting officer is the assigned one. A different officer gets 409, from the same domain rule the unit tests cover.
- **Who is acting.** The acting user's id always comes from the token's `sub`, never from the request body.
- **Tests.** Integration tests (`SecurityTests`, Testcontainers Postgres, real tokens from dev-login): another claimant's claim → 404; a claimant settles → 403; two officers pick up the same claim at once → one 204, one 409; no token → 401; a signed token with an unknown role sees no claims; dev-login is 404 outside Development or without the switch.

## Kafka

Locally Kafka runs PLAINTEXT with no auth (docker compose). In production: SASL/SSL (TLS in transit plus client authentication), plus ACLs so only the claims API can write to `claims.events` and only the projection consumer group can read it. Events already carry no personal data (see decisions.md).
