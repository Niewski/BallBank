# ADR-0001: Event sourcing with Marten

- **Status:** Accepted
- **Date:** 2026-09-24

## Context

The product is the history. The questions a league actually asks — "what did Jacob owe on October 1?",
"who confirmed that payment and when?", "why is the pot $10 short?" — are questions about the past,
not about current state. A mutable `accounts` table with a `balance` column answers none of them
without a separate audit table that has to be kept in sync by hand.

We also want read models we can reshape freely (a member statement, a treasurer dashboard, a
delinquency digest) without migrating the write model each time.

## Decision

Each `MemberAccount` (and later each `Season`) is an event stream. State is rebuilt by applying
events; commands are validated against that state and produce new events. Read models are
projections, rebuildable from the stream.

The event store is [Marten](https://martendb.io) on PostgreSQL, with [Wolverine](https://wolverinefx.net)
for command handling, the transactional outbox and scheduled messages. Marten gives us the stream
store, live and inline/async projections, conjoined multi-tenancy, optimistic concurrency and
metadata (causation, correlation, headers) in one library, on a database we can run for free.

## Alternatives considered

- **CRUD tables plus an audit log.** Simplest to start; the audit log becomes the second source of
  truth and drifts. Temporal questions need ad-hoc queries over the log anyway.
- **EventStoreDB / Kurrent.** A dedicated event store with excellent tooling, but a separate piece
  of infrastructure to run and pay for, and we would still need PostgreSQL for read models.
- **Hand-rolled event table.** Tempting for a small system; projections, tenancy, versioning and
  the daemon are exactly the parts that are tedious to get right.

## Consequences

- Balances are never stored; they are derived (see `MemberAccount.Balance`).
- Events are immutable and versioned by type; changing shape means a new type and an upcaster.
- Some read models are eventually consistent (ADR-0006), which the UI must be honest about.
- Learning curve for contributors; mitigated by keeping the domain pure and the tests in
  given/when/then form.
