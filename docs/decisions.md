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

## Dashboards

## Frontend
