# ADR-0006: Inline projections by default, one async projection

- **Status:** Accepted; amended 2026-09-26 (the confirmation queue is a query, not a projection)
- **Date:** 2026-09-24

## Context

At this scale every projection could be inline (updated in the same transaction as the events) and
nobody would notice. But the treasurer dashboard aggregates across every account in a league, and an
async projection is the honest design for a multi-stream view.

## Decision

`MemberStatement` is inline: a member sees their own confirmation the moment it happens. `LeaguePot`
is async, run by Marten's projection daemon, and its lag is measured and published.

As built (M3):

- **The daemon** runs inside the API in Marten's `Solo` mode: one node runs it, as one node runs
  Wolverine's durability agent for now ([ADR-0007](0007-scheduled-work-under-scale-to-zero.md)).
  Its progress is recorded in the database, so it carries on from there whenever the app starts. The
  integration tests host the same API, so they run the same daemon.
- **`LeaguePot`** is one document per season, keyed by the season id, built from every
  `MemberAccount` stream of that season. A grouper sends each account event to its season by reading
  the account's opening event within the tenant. The document keeps sums per account and computes
  its figures and balances from them, and records the sequence and timestamp of the last event
  applied: the "as of" the dashboard shows. No command writes it. Delinquent (a balance above zero
  once the earliest due date has passed) is worked out when the dashboard is read, from the day the
  API's clock reads in UTC.
- **The lag** is the time from an event being recorded to the daemon applying it, recorded on the
  histogram `ballbank.projection.lag` (seconds, meter `BallBank.Projections`) with the tags
  `tenant.id`, `projection` and `event.type`. `ServiceDefaults` exports the meter with the rest.
- **A rebuild** replays the events into a fresh document: `projections rebuild -p LeaguePot`, served
  by the same image ([runbook](../runbook.md#rebuild-a-projection)).

The confirmation queue is not a projection. Every pending attestation, with its status, already sits
on a `MemberStatement` line, updated inline by the same transaction that confirms or rejects it. The
queue is a query over the season's statements, like the ledger: one read of a few dozen documents,
filtered to pending lines and ordered oldest first. A separate `ConfirmationQueue` document would be
a second, multi-stream copy of the same facts to keep in step, for no read it makes faster.

## Consequences

The dashboard may trail by a moment after a confirmation; the UI says so. Running the daemon under
scale-to-zero needs care (ADR-0007). Rebuilding `LeaguePot` is a documented runbook step.

The confirmation queue is never behind: whatever a treasurer confirms or rejects has left the queue
by the time the answer returns. If a season ever holds so many statements that reading them all is
slow, the queue becomes a projection then, rebuilt from events like any other.
