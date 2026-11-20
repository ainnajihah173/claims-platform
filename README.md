# Claims Platform

## What it is
An MVP for insurance claims across six APAC markets. Claimants submit claims and answer questions. Officers pick claims up, set reserves and settle or reject them. Managers see exposure and workload.
ASP.NET Core API + Postgres, events through Kafka (outbox → idempotent consumer), Angular frontend.

## How to run
Needs: Docker, .NET 10 SDK, Node 22.22+ (or 24.15+ / 26+).

Once, after cloning: `cp .env.example .env`, then set `JWT_KEY` in `.env` (random, 32+ chars).

Then, from the repo root:
```bash
docker compose up -d --build          # Postgres, Kafka and the API
cd frontend/claims-web && npm install && npm start
```

To run the API from source instead (to debug it), set the key once with
`dotnet user-secrets set Jwt:Key "<random, 32+ chars>" --project backend/Claims.Api`, then
`docker compose up -d postgres kafka` and `dotnet run --project backend/Claims.Api`. Both use port 5249, so stop the container first (`docker compose stop api`).

| What | URL |
|---|---|
| App | http://localhost:4200 |
| API docs (Scalar) | http://localhost:5249/scalar |

The API creates and seeds the database on first start. Tests: `dotnet test` (Docker must be running, the API tests start their own Postgres containers, and the Kafka test its own Kafka).
On the very first start the consumer logs one "unknown topic" error until the publisher creates `claims.events`. It recovers on its own.
The API container also logs one `libgssapi_krb5.so.2` error at start: Npgsql looks for Kerberos, which isn't used here. Harmless.
Reset everything (after a schema or event change, there are no migrations): `docker compose down -v && docker compose up -d --build`.

## Seeded users
Dev login only: pick a user, no password. The API still issues and checks a real signed JWT.

| User | Role | Team | Id |
|---|---|---|---|
| Aisha Claimant | Claimant | | `00000000-0000-0000-0000-000000000001` |
| Ben Claimant | Claimant | | `00000000-0000-0000-0000-000000000002` |
| Chen Officer | Officer | Motor | `00000000-0000-0000-0000-000000000011` |
| Devi Officer | Officer | Motor | `00000000-0000-0000-0000-000000000012` |
| Eric Officer | Officer | Property | `00000000-0000-0000-0000-000000000013` |
| Farah Manager | Manager | Claims | `00000000-0000-0000-0000-000000000021` |

Eight seeded claims cover every status, three of them overdue.

## Demo script
Login is per browser tab, so keep Aisha in one tab and Chen in another. Notifications have no screen yet, so step 8 goes through Scalar.

**Calling the API in Scalar:** open `POST /api/auth/dev-login`, send `{ "userId": "<id from the table>" }`, copy `token`. On each later call add the header `Authorization: Bearer <token>`.

1. **Submit** (app, tab 2, log in as Aisha): **Submit a claim** → Motor, Malaysia, policy `MY-MOT-1099`, "Bumper dented while parked" → **Submit claim**. The new claim opens as Submitted.
2. **Pick up** (app, tab 1, log in as Chen): the new claim is under the **To pick up** card. Open it → **Pick up this claim**. Status: In review.
3. **Request info** (Chen): under **Next step**, type "Please send a photo of the bumper." → **Request info**. Status: Awaiting info.
4. **Answer** (tab 2, Aisha, refresh): open the claim under the **Needs your answer** card → type an answer → **Send answer**. Status: In review.
5. **Set reserve** (Chen, refresh the page): **Amount** `1000` → **Set reserve**.
6. **Settle** (Chen): try `1200` → **Settle** is refused (more than the reserve). Then `800` → **Settle**. Status: Settled, and the timeline shows every step.
7. **Exposure** (Chen): **Dashboard** (it refreshes every 15 s). Malaysia / Motor went from 2 open / 1,500.00 to 3 / 1,500.00 on submit, 3 / 2,500.00 on reserve, and back to 2 / 1,500.00 on settle (the 1,000 reserve is released, not the 800 paid).
8. **Notifications** (Scalar, as Aisha): `GET /api/notifications` shows the info request and "settled for 800.00 MYR".

Also worth a look: the Workload table on the Dashboard as Chen or Farah; as Aisha, any dashboard call → 403 and another claimant's claim → 404; an 11th claim from one claimant within a minute → 429.

## Architecture
```
 Angular SPA ──REST + JWT──▶ Claims.Api ──EF Core──▶ Postgres
 (:4200, /api proxied)       (:5249)                 claims, history, outbox,
                                                     exposure_summary, notifications
                                │                        ▲
                         OutboxPublisher         ProjectionConsumer
                                │                        │
                                └──▶ Kafka ──────────────┘
                                   claims.events
                                   (key = claimId)
```
- **REST** for every user action: the user needs an answer straight away (done, or refused and why).
- **Kafka** for the side effects that can lag a second: the exposure read model and claimant notifications. New readers (email, audit) add a consumer group, with no change to the claims code.
- The **outbox** row is written in the same transaction as the claim, so an event is never lost or sent for a change that rolled back. The consumer skips events it has already seen.

One service (modular monolith), one database. Details in `docs/decisions.md`.

## Docs
- [docs/assumptions.md](docs/assumptions.md): what I assumed where the brief was silent
- [docs/decisions.md](docs/decisions.md): status flow, entities, architecture, events, dashboards, frontend
- [docs/security.md](docs/security.md): threats, MVP controls, production controls
- [AI_JOURNAL.md](AI_JOURNAL.md): every prompt, what the AI gave, what I kept or changed and why

## Cut / next
- Notifications screen in the UI (the endpoint exists, use Scalar).
- Exposure trend over time (today only the current totals).
- Currency conversion for a group-wide total (needs FX rates).
- Real OIDC (Entra ID / Keycloak) instead of dev login.
- Rate limiting at the gateway: today only `POST /api/claims` is limited (10 per claimant per minute), in memory per API instance.
- Retry limit + dead-letter topic for the consumer: today a message that always fails blocks its partition.
- Attachments (photos, quotes).
- The web app in compose too: compose starts Postgres, Kafka and the API, the web app still runs with `npm start`.
- Pause the overdue clock while a claim is AwaitingInfo.
- Reassign a claim to another officer: today only the assigned officer can act, so a claim stalls if they leave.
- No test for duplicate delivery: `KafkaTests` proves outbox → Kafka → exposure + notification end to end (Testcontainers), but "duplicate skipped" was only checked by hand (all 27 events re-sent, totals unchanged).

## Time spent
**Core (Steps 1–8) in about 2¾ hours** (21:55 → 00:42): submittable at that point, inside the brief's 2–3 h target. Then the extras, a frontend redesign and final docs, about 3¾ hours in total (done 01:42), inside my own 4-hour budget and the 5 h hard cap. Planning and tool setup happened before the timer. From the commit log:

| Step | Done at | Min |
|---|---|---|
| Scaffold, compose, AI rules | 22:08 | ~15 |
| Domain + tests, review fixes | 22:42 | 34 |
| Database + API | 23:15 | 33 |
| Security | 23:45 | 30 |
| Kafka events, review fixes | 00:10 | 25 |
| Dashboards | 00:22 | 12 |
| Angular | 00:34 | 12 |
| README, fresh-clone test, journal | 00:42 | 8 |
| Extras (submit form, dashboard screen, Dockerised API, Kafka test, rate limit) and the frontend redesign, side by side | 01:02 | ~20 |
| README update, second fresh-clone test | 01:15 | ~10 |
| Timeline fix (last card cut off) | 01:24 | ~9 |
| Journal rework, final README | 01:42 | ~18 |

**Why backend first.** The rules that matter (status flow, who may act, reserve and settle limits, ownership, no lost events) live in the backend and can be proven by tests. The UI is a thin layer over the API, and if it had run short the whole flow could still be shown in Scalar. Building the UI first would have meant building it against an API that didn't exist yet.
