# ADR-0005: Idempotency and optimistic concurrency

- **Status:** Accepted
- **Date:** 2026-09-24; layer two accepted 2026-09-26; layer three accepted 2026-09-26; webhooks named 2026-10-01; purging built 2026-10-01

## Context

Mobile clients retry. Two treasurers may act at once. Webhooks are delivered at least once. None of
these may produce a duplicate fact in the books.

## Decision

Three layers that converge:

1. **Inside the aggregate:** every command carries the id of the fact it creates; a replay returns
   no event.
2. **At the HTTP edge:** every mutating request carries an `Idempotency-Key`; the stored response is
   returned on a retry. Webhooks carry no key of ours: Twilio's status callbacks and inbound replies
   (`/webhooks/twilio/…`) are idempotent by what they record (see "Webhooks in detail").
3. **Between writers:** commands carry the expected stream `Version`; a stale version is a
   `409` with the current version, and the client re-reads.

### Layer two in detail

- An endpoint opts in by taking an `IdempotentRequest`; Wolverine.HTTP middleware
  (`IdempotencyMiddleware`) runs in front of every such endpoint.
- No `Idempotency-Key` header: `412 Precondition Failed`, as problem details.
- The key is scoped to the league (the tenant) and the caller's subject. The answer is kept as a
  tenant-scoped `IdempotencyRecord` document: a SHA-256 fingerprint of the method, path and body,
  and the response status and body. Every idempotent endpoint so far has the league in its route;
  one without would keep its records in the default tenant.
- The record is written in the same Marten session, and so the same transaction, as the events the
  request records. A stored response exists only if the command committed; a refused or failed
  command stores nothing, and a corrected retry under the same key goes through.
- The same key and fingerprint again: the stored response, without touching the aggregate.
- The same key with a different fingerprint: `422 Unprocessable Content`.
- Two requests racing on one key: the loser's commit collides with the winner's (on the record or
  on the streams), and it re-reads the record and answers with the stored response. "On the streams"
  includes a stream appended to since it was read (Marten's `ConcurrencyException`), which a bulk
  assessment meets because it carries no expected version: whoever did not commit gets a `409` and
  can assess again, since accounts already carrying the assessment skip it.
- Records are purged by the tick (ADR-0007), not by a person: each hourly tick deletes, for each league
  it visits (one with an open season), the league's `IdempotencyRecord`s written more than
  `Retention:Days` ago (90 unless set; the setting refuses anything under 14), and the `Notification`
  records with them, by the same age. It deletes inside that league's own session, so no league's records
  are another's to delete, and the free database tier does not fill with answers nobody will ask for
  again. A client retries within minutes, so an answer that old is one
  no one asks for; if its key did come back, layer one would answer it, recording no second fact, in
  place of the stored answer. The age is a floor for notifications as much as for answers: a
  notification's id is its dedupe key, claimed until the record is purged, so a record must outlive the
  week a digest is due in and the week a reminder's stage lasts.
- The web client generates a fresh key per submission and reuses it when retrying that submission
  (`web/src/lib/idempotency.ts`).

### Webhooks in detail

Twilio calls back at least once, in any order, and sends no event id we could key on. Both
endpoints are therefore idempotent by what they record, not by a stored answer:

- **A signature, not a key.** Every call must carry a valid `X-Twilio-Signature` (HMAC-SHA1 of the
  public URL and the sorted form fields, under the deployment's auth token); otherwise `403`, with
  nothing read from the body. The URL is rebuilt from `Twilio:StatusCallbackBaseUrl`, so that setting
  must be the address Twilio signs.
- **A status callback** (`POST /webhooks/twilio/status?league=&notification=`) names its
  `Notification` in the query string, which BallBank wrote and Twilio signed, so the tenant never
  comes from the form. Statuses only move forward: `queued`, `sending` and `sent` change nothing, and
  `delivered`, `undelivered` or `failed` settle a notification that was `Sent` and are never overwritten.
  A callback delivered twice, or late, records one state.
- **An inbound reply** (`POST /webhooks/twilio/inbound`) is read by its trimmed body, whole and
  case-insensitively. STOP keeps the first `PhoneOptOut` it made for the number (a second STOP changes
  nothing); START deletes it, so a second START changes nothing either. Anything else is answered
  `200` and ignored.
- Only a `Sent` notification is settled, so nothing is ever moved back: the send handler saves `Sent`
  after Twilio accepts a text, and a callback that beats it (a window of a few milliseconds) finds the
  notification `Pending` and changes nothing. Twilio reports each state once, so that text stays
  `Sent`, which the treasurer reads as "in flight". Closing the window would take a revision check on
  a document only these two write, and is not worth it yet.
- Twilio's signature carries no timestamp, so a captured signed request could be replayed. The worst a
  replay does is repeat what it said: a replayed START lifts a later STOP, but only where a member's
  consent is still on record. We do not guard against it (Twilio gives inbound messages a `MessageSid`
  that could be remembered, if it ever matters).

### Layer three in detail

- A command about an existing stream carries `version` **in its JSON body**: the stream version the
  client last read (the `version` every statement and ledger row serves). This follows Wolverine's
  aggregate-handler convention of a `Version` on the command, and keeps the precondition beside the
  command it guards. The first such command is attesting a payment (`POST
  /leagues/{leagueId}/accounts/{accountId}/attestations`).
- No `version`: `412 Precondition Failed`, as problem details titled `Version required`, the same
  status as a missing `Idempotency-Key`.
- A stale `version`: `409 Conflict`, as problem details with `type` `version-conflict` and
  `currentVersion`, the stream's version now. Nothing is recorded, so neither is an answer under the
  `Idempotency-Key`; the web reloads the statement and asks the person to try again.
- The endpoint loads the stream with `FetchForWriting`, compares versions before appending, and
  Marten checks again on commit. A writer who commits in between turns the commit into a
  `ConcurrencyException`, which `IdempotentRequest.CommitAsync` treats as a collision: the stored
  answer if it was this very request, the `version-conflict` otherwise.
- Layer one comes first: a command whose fact is already recorded (the same attestation id again)
  is a no-op answered with the current state, whatever version it carries. A double submission
  records one fact even when its second copy is stale.
- Commands that create a stream (opening a season) carry no version: a deterministic stream id
  already makes a second writer collide.

## Alternatives considered

- **A plain ASP.NET Core middleware buffering the response.** It could capture any response, but it
  could not write the record in the transaction that records the events, so a stored response could
  exist for a command that never committed, or the reverse.
- **Keying on the key alone, or the key and the league.** Two callers who happen to pick the same
  key would get each other's answers.
- **The expected version in an `If-Match` header (with an ETag on reads).** Standard HTTP, but it
  puts half of the command outside its body, needs ETags on every read that feeds a command, and
  is not what Wolverine's aggregate handlers expect. A missing precondition would still be `412`
  either way.

## Consequences

Clients must generate ids and keys, and send the version they read with every command about an
existing stream; the API rejects mutating requests without them (`412`).
Tests must prove: a double-submitted attestation yields one event; two concurrent confirmations
yield one `PaymentConfirmed`. Every later mutating endpoint adopts layer two by taking an
`IdempotentRequest` and committing through it.
