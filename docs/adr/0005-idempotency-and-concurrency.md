# ADR-0005: Idempotency and optimistic concurrency

- **Status:** Proposed
- **Date:** 2026-09-24

## Context

Mobile clients retry. Two treasurers may act at once. Webhooks are delivered at least once. None of
these may produce a duplicate fact in the books.

## Decision (proposed)

Three layers that converge:

1. **Inside the aggregate:** every command carries the id of the fact it creates; a replay returns
   no event.
2. **At the HTTP edge:** every mutating request carries an `Idempotency-Key`; the stored response is
   returned on a retry. Webhooks use the provider's event id.
3. **Between writers:** commands carry the expected stream `Version`; a stale version is a `409`
   with the current version, and the client re-reads.

## Consequences

Clients must generate ids and keys; the API rejects mutating requests without them (`412`).
Tests must prove: a double-submitted attestation yields one event; two concurrent confirmations
yield one `PaymentConfirmed`.
