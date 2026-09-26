# ADR-0005: Idempotency and optimistic concurrency

- **Status:** Accepted for layers one and two (the header, the record, the status codes); layer three
  (expected `Version`) Proposed
- **Date:** 2026-09-24; layer two accepted 2026-09-26

## Context

Mobile clients retry. Two treasurers may act at once. Webhooks are delivered at least once. None of
these may produce a duplicate fact in the books.

## Decision

Three layers that converge:

1. **Inside the aggregate:** every command carries the id of the fact it creates; a replay returns
   no event.
2. **At the HTTP edge:** every mutating request carries an `Idempotency-Key`; the stored response is
   returned on a retry. Webhooks use the provider's event id.
3. **Between writers (proposed):** commands carry the expected stream `Version`; a stale version is a
   `409` with the current version, and the client re-reads.

### Layer two in detail

- An endpoint opts in by taking an `IdempotentRequest`; Wolverine.HTTP middleware
  (`IdempotencyMiddleware`) runs in front of every such endpoint.
- No `Idempotency-Key` header: `412 Precondition Failed`, as problem details.
- The key is scoped to the league (the tenant) and the caller's subject. The answer is kept as a
  tenant-scoped `IdempotencyRecord` document: a SHA-256 fingerprint of the method, path and body,
  and the response status and body.
- The record is written in the same Marten session, and so the same transaction, as the events the
  request records. A stored response exists only if the command committed; a refused or failed
  command stores nothing, and a corrected retry under the same key goes through.
- The same key and fingerprint again: the stored response, without touching the aggregate.
- The same key with a different fingerprint: `422 Unprocessable Content`.
- Two requests racing on one key: the loser's commit collides with the winner's (on the record or
  on the streams), and it re-reads the record and answers with the stored response.
- Records are kept. Purging old ones is an operational task (M3 runbook).
- The web client generates a fresh key per submission and reuses it when retrying that submission
  (`web/src/lib/idempotency.ts`).

## Alternatives considered

- **A plain ASP.NET Core middleware buffering the response.** It could capture any response, but it
  could not write the record in the transaction that records the events, so a stored response could
  exist for a command that never committed, or the reverse.
- **Keying on the key alone, or the key and the league.** Two callers who happen to pick the same
  key would get each other's answers.

## Consequences

Clients must generate ids and keys; the API rejects mutating requests without them (`412`).
Tests must prove: a double-submitted attestation yields one event; two concurrent confirmations
yield one `PaymentConfirmed`. Every later mutating endpoint adopts layer two by taking an
`IdempotentRequest` and committing through it.
