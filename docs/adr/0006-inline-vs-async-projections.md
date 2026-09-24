# ADR-0006: Inline projections by default, one async projection

- **Status:** Proposed
- **Date:** 2026-09-24

## Context

At this scale every projection could be inline (updated in the same transaction as the events) and
nobody would notice. But the treasurer dashboard aggregates across every account in a league, and an
async projection is the honest design for a multi-stream view.

## Decision (proposed)

`MemberStatement` and `ConfirmationQueue` are inline: a member sees their own confirmation the
moment it happens. `LeaguePot` is async, run by Marten's projection daemon, and its lag is measured
and published.

## Consequences

The dashboard may trail by a moment after a confirmation; the UI says so. Running the daemon under
scale-to-zero needs care (ADR-0007). Rebuilding `LeaguePot` is a documented runbook step.
