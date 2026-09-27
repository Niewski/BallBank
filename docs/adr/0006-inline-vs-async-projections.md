# ADR-0006: Inline projections by default, one async projection

- **Status:** Proposed; amended 2026-09-26 (the confirmation queue is a query, not a projection)
- **Date:** 2026-09-24

## Context

At this scale every projection could be inline (updated in the same transaction as the events) and
nobody would notice. But the treasurer dashboard aggregates across every account in a league, and an
async projection is the honest design for a multi-stream view.

## Decision (proposed)

`MemberStatement` is inline: a member sees their own confirmation the moment it happens. `LeaguePot`
is async, run by Marten's projection daemon, and its lag is measured and published.

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
