# Decisions

## Status flow

| From | Action | To | Who |
|---|---|---|---|
| (new) | Submit | Submitted | Claimant |
| Submitted | Assign | InReview | Officer |
| InReview | RequestInfo | AwaitingInfo | Assigned officer |
| AwaitingInfo | ProvideInfo | InReview | Owner |
| InReview / AwaitingInfo | SetReserve | (same) | Assigned officer |
| InReview | Settle | **Settled** (final) | Assigned officer |
| InReview / AwaitingInfo | Reject | **Rejected** (final) | Assigned officer |

Any other change is refused. Settled and Rejected are final: no action works on them.
Extra guards: a claimant can't pick up their own claim. Text is required for the question, the answer and the reject reason. **The settled amount must be greater than zero and no more than the reserve.** A higher payout needs a recorded reserve increase first, so exposure is never understated. Settling below the reserve is fine. Amounts have at most 2 decimal places (stored as `numeric(18,2)`). Reject also works from AwaitingInfo, so a claimant who never answers can't keep a claim open (and its reserve in exposure) forever.

## Entities

```
User 1───* Claim (as claimant)        Claim 1───* ClaimHistory
User 1───* Claim (as assigned officer) User  1───* Notification ───1 Claim
```

- **User**: claimant, officer or manager. Officers and managers have a team.
- **Claim**: type (Motor/Property), market (MY, SG, HK, AU, TH, ID; the market sets the currency), policy number, description, status, reserve, settled amount, submitted/closed time, `Version` (bumped on every change, used as a concurrency token).
- **ClaimHistory**: append-only timeline row: `EventType`, `OldValue`, `NewValue`, `Note`, `ActorId`, `At`. It covers submit, assignment, status changes, reserve changes, questions and answers.
- **Notification**: an in-app message to a claimant about one claim (info requested, settled, rejected). Built from events, not written by Claim.

Money is a `decimal` in the claim's currency. A claim never mixes currencies.

**Why the rules live in the domain.** All status changes go through `Claim` methods with private setters. As long as every write loads the claim and calls a `Claim` method, no caller (API, seed data, consumers) can skip a rule. A bulk SQL update (`ExecuteUpdateAsync`) or raw SQL would bypass the rules, the history and the events, so we don't use them on claims. Double pick-up is stopped by the `Version` concurrency check instead. The rules are plain C#, so 76 fast unit tests cover every (status, action) pair without a database or HTTP. Each method also checks who is acting (assigned officer or owning claimant). The API only adds the role check.

**Why Q&A are history events.** A question is an `InfoRequested` row and the answer is an `InfoProvided` row on the same timeline. No separate InformationRequest table. The status already shows whether an answer is open (AwaitingInfo), only one question can be open at a time, and the timeline UI reads a single list. A separate table would duplicate that state, and the two copies could drift apart.

**Domain events.** Every action adds one `ClaimEvent` to `Claim.DomainEvents` (type, ids, old/new status, market, type, currency, old/new reserve, settled amount). Events carry no notes, policy number or description, so no personal data can reach Kafka.

**Overdue.** `IsOverdue(now, targetDays)`: an open claim is overdue when more than `targetDays` have passed since submit. The target is passed in (from config, per claim type). The clock doesn't pause during AwaitingInfo (assumption 8).

## Architecture

**One service (modular monolith).** One ASP.NET Core API owns claims, notifications and dashboards, with one Postgres database. Folders (`Domain`, `Data`, `Controllers`) keep the parts apart. At this size, separate services would add network calls, deployments and data sync without any gain. Kafka (Step 5) already separates the read models from the write path, so a part can be split out later if it needs to scale on its own.

**REST for user actions.** Every user action is one HTTP call that answers right away: `POST /api/claims`, `POST /api/claims/{id}/assign|request-info|provide-info|settle|reject`, `PUT /api/claims/{id}/reserve`. Actions are named verbs, not a generic `PATCH status`, so each one maps to exactly one `Claim` method and one rule set. Errors are problem details: 400 bad input, 404 unknown claim, 409 when the claim's state refuses the action or someone else changed it first.

**The contract = OpenAPI generated from the code.** `Microsoft.AspNetCore.OpenApi` builds `/openapi/v1.json` from the controllers and DTOs, and Scalar (`/scalar`) shows it and lets you try each call. Code-first keeps the contract and the code in step with no extra work for one developer. In a bigger team I'd go contract-first: agree the OpenAPI file, then generate server stubs and the Angular client, so frontend and backend can work in parallel.

**DTOs block mass-assignment.** Requests are small records with only the fields a caller may set (type, market, policy number, description, amount, text). Status, reserve, officer, claimant and version can't be sent at all. The acting user's id comes from the JWT `sub`, never from the body (see security.md). Responses are DTOs too, so entities never leave the API. Data annotations check shape at the edge: required non-blank text with a max length, enums must be defined names (`JsonStringEnumConverter` + `[EnumDataType]`), and amounts must fit `numeric(18,2)` (`[Money]`). The domain still checks the rules.

**The `Version` check stops double pick-up.** `Claim.Version` goes up on every action and is an EF concurrency token. Every write loads the claim, calls a `Claim` method and saves. If two officers load the same Submitted claim and both pick it up, the second `UPDATE ... WHERE Version = @old` matches no row → `DbUpdateConcurrencyException` → 409, and the first officer keeps the claim. If the second request loads after the first has saved, `Assign` itself refuses (no longer Submitted) → 409. Why not `ExecuteUpdateAsync(... WHERE AssignedOfficerId IS NULL)`? It is also atomic, but it writes the row directly and skips the domain rules (self-pick-up guard), the history row and the domain event.

**Persistence.** Enums are stored as text (reordering an enum never remaps saved rows). Money is `numeric(18,2)`. History is mapped through the `_history` field. `DomainEvents` isn't mapped (Step 5 turns events into outbox rows). Indexes: `(Status, AssignedOfficerId)` for the queues, `(ClaimantId)` for "my claims". The `notifications`, `outbox`, `processed_events` and `exposure_summary` tables exist now, so `EnsureCreated()` builds the whole schema once. Shortcut: `EnsureCreated()` + seed at startup, no migrations.

## Events (Kafka)

**What goes over Kafka, and why.** Every claim action publishes one event to the `claims.events` topic. User actions stay synchronous REST (they need an answer straight away). Kafka carries the side effects that can lag a second behind: the exposure read model (`exposure_summary`) and claimant notifications. The write path doesn't know who listens, so a new reader (email, audit, a split-out reporting service) only needs a new consumer group, with no change to the claims code.

**The outbox stops lost events.** Writing to the database and then to Kafka is two systems: a crash in between loses the event, or sends one for a change that rolled back. Instead, `AppDbContext.SaveChanges` turns each `Claim` domain event into an `outbox` row in the same transaction as the claim change. Both commit or neither does. `OutboxPublisher` (background service) reads unpublished rows every second, oldest first, sends each one, waits for the broker ack (`Acks = All`), then sets `PublishedAt`. If Kafka is down, rows wait and go out once it's back. `EnableIdempotence = true` stops the producer's own retries from writing a message twice.

**At-least-once + an idempotent consumer.** A crash after the ack but before `PublishedAt` is saved sends a row again, so Kafka can deliver an event twice. `ProjectionConsumer` handles that. Auto-commit is off. For each message, one `SaveChanges` (one transaction) inserts the `eventId` into `processed_events` (primary key), updates `exposure_summary` and adds the claimant notification (InfoRequested, Settled, Rejected). An `eventId` it has seen before is skipped. The Kafka offset is committed only after the DB commit, so a crash replays the message instead of losing it, and the replay is skipped. Checked by marking all 27 outbox rows unpublished: all 27 went out again (54 messages on the topic), and events, notifications and exposure didn't change. Shortcut: a failed message is retried every 5 s with no limit and no dead-letter topic, so a message that always fails blocks its partition (for example an event for a claim that isn't in this database, or an exposure total past `numeric(18,2)`, about 10¹⁶ in one group).

**No backfill.** Exposure is built only from events. A database created before Step 5 has claims with no outbox rows, so its exposure starts at zero and settling one of those claims makes it negative. Instead of a backfill, reset once with `docker compose down -v` (it wipes Postgres and Kafka together, so the topic and the consumer offsets start over too). That goes with the `EnsureCreated` shortcut: with no migrations, any schema or event change means a reset. With migrations, a one-off backfill would rebuild `exposure_summary` from the `claims` table.

**Other known limits** (marked `shortcut:` in the code): one publisher per database; no index on `outbox.PublishedAt`, and published rows and `processed_events` are never cleaned up; the publisher orders rows by the app clock, not by insert order. All of these are fine with one API instance and dev-sized data.

**Key = claimId keeps order.** Kafka keeps order only within a partition. With the claim id as the key, all events of one claim go to the same partition, in the order they happened (submit before settle). Different claims spread over the 3 partitions.

**Exposure maths** (per market + currency + type, so currencies never mix): Submitted → open count +1. ReserveSet → add `Reserve − OldReserve`. Settled / Rejected → open count −1 and subtract the claim's last `Reserve`, not the settled amount. Exposure is the reserve being held, so a claim with a 1,000 reserve settled for 800 removes 1,000.

**No directly identifying data in events.** The payload is `eventId`, `eventType`, `schemaVersion` (1), `occurredAt`, `claimId`, `actorId`, old/new status, market, type, currency, old/new reserve and settled amount. Only opaque ids, statuses and amounts: no names, notes, questions, answers, policy number or description (a test checks this). `claimId` and `actorId` can still be linked to a person through our database, so under GDPR/PDPA they count as pseudonymous data: the topic still needs access control (see security.md). The consumer finds the claimant through `claimId` in our own database. `schemaVersion` lets a consumer tell old messages from new ones if the payload ever changes.

## Dashboards

Both endpoints are staff only (`StaffOnly`); a claimant gets 403.

**Exposure = event-fed read model.** `GET /api/dashboard/exposure` reads `exposure_summary` as it stands: one row per market + currency + type with the open count, total reserve and `asOf` (when that row last changed). Totals are never added across currencies. The consumer builds it from events (see Events), so it lags the claims by about a second, and on a just-seeded database it is empty until the consumer catches up. Reading a ready-made table keeps the query cheap however many claims there are, and the totals never need a scan of every open claim.

**Workload = live query.** `GET /api/dashboard/workload` queries `claims` on each call. Per staff user (officers and managers, assumption 6) it returns: open, AwaitingInfo, overdue (same `Claim.IsOverdue` rule and config as the claims list), oldest open age in days, the number closed in the last 30 days and their average days from submit to close. It also returns the unassigned count (Submitted, no officer, the same filter as the unassigned queue). It is a live query because officers act on it ("who has room for this claim?") and it must match the queue they see. A lagging read model could show a claim as unassigned after someone picked it up. It is cheap: it reads only assigned open claims and those closed in the last 30 days. Shortcut: they are loaded into memory so the overdue rule is reused, not rewritten in SQL. Move the counts to a SQL `GROUP BY` once there are tens of thousands of open claims.

**UI polls and shows "as of".** No push (SignalR/websockets). The dashboard screen re-fetches both endpoints on a timer and shows each `asOf`: workload's is the server time of the query, exposure's is per row. A stale exposure row is then visible instead of hidden.

## Frontend
