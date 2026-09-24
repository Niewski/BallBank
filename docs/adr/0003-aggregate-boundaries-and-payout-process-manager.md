# ADR-0003: Aggregate boundaries and the payout process manager

- **Status:** Proposed
- **Date:** 2026-09-24

## Context

Per-member invariants (an attestation is confirmed once; a rejected one cannot be confirmed) are
local to one member's books. One invariant is not: total payouts cannot exceed the confirmed pot,
which is a sum across every member's account in a season.

## Decision (proposed)

Two stream types: `MemberAccount` (league × season × member) and `Season` (league × season).
The cross-stream rule is enforced by a process manager reacting to `SeasonClosed`: it reads the
`LeaguePot` projection, computes payouts from the payout structure and declared standings, and issues
`RecordPayout` to each account. Eventual consistency is accepted and surfaced in the UI as
"payouts being recorded".

## Alternatives to weigh before accepting

- **One fat `Treasury` stream per season** holding every member's events. Makes the pot rule a
  local invariant, but serialises every member's writes through one stream and makes per-member
  history noisier.
- **Wolverine's dynamic consistency boundary (`[BoundaryModel]`)** across the affected streams at
  close time. Worth a spike: it may give the strong guarantee without the fat stream.

## Consequences

Season close is a multi-step, replayable process rather than one transaction; it needs an
idempotent design and a visible status.
