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
| InReview | Reject | **Rejected** (final) | Assigned officer |

Any other change is refused. Settled and Rejected are final: no action works on them.
Extra guards: a claimant can't pick up their own claim. Text is required for the question, the answer and the reject reason. **The settled amount must be greater than zero and no more than the reserve.** A higher payout needs a recorded reserve increase first, so exposure is never understated. Settling below the reserve is fine.

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

**Why the rules live in the domain.** All status changes go through `Claim` methods with private setters. The API, the seed data and later consumers can't skip a rule, because no other code path can change the state. The rules are plain C#, so 73 fast unit tests cover every (status, action) pair without a database or HTTP. Each method also checks who is acting (assigned officer or owning claimant). The API only adds the role check.

**Why Q&A are history events.** A question is an `InfoRequested` row and the answer is an `InfoProvided` row on the same timeline. No separate InformationRequest table. The status already shows whether an answer is open (AwaitingInfo), only one question can be open at a time, and the timeline UI reads a single list. A separate table would duplicate that state, and the two copies could drift apart.

**Domain events.** Every action adds one `ClaimEvent` to `Claim.DomainEvents` (type, ids, old/new status, market, type, currency, old/new reserve, settled amount). Events carry no notes, policy number or description, so no personal data can reach Kafka.

**Overdue.** `IsOverdue(now, targetDays)`: an open claim is overdue when more than `targetDays` have passed since submit. The target is passed in (from config, per claim type). The clock doesn't pause during AwaitingInfo (assumption 8).

## Architecture

## Events (Kafka)

## Dashboards

## Frontend
