# Domain model

Vocabulary is defined in [CONTEXT.md](../CONTEXT.md). This page is the shape: which streams exist,
which events they hold, and which invariants each command enforces.

## Streams

### `MemberAccount` — one per league × season × member

Where the money invariants live.

| Command | Event | Invariant / behaviour |
|---|---|---|
| `OpenAccount` | `AccountOpened` | One per member per season. Needs a season. |
| `AssessDues` | `DuesAssessed { assessmentId, amount, dueDate, memo }` | Amount > 0. Same `assessmentId` again → no event (idempotent). |
| `AttestPayment` | `PaymentAttested { attestationId, amount, rail, reference }` | Amount > 0. Reference required unless the rail is Cash. Same `attestationId` again → no event. Does **not** change the balance. |
| `ConfirmPayment` | `PaymentConfirmed { attestationId }` | Attestation must exist and be pending. Already confirmed → no event. Rejected → refused. Moves the balance. |
| `RejectPayment` | `PaymentRejected { attestationId, reason }` | Reason required. Already rejected → no event. Confirmed → refused (post an adjustment instead). |
| `PostAdjustment` *(planned)* | `AdjustmentPosted { amount, reason }` | Reason required. Treasurer only. |
| `RecordPayout` *(planned)* | `PayoutRecorded { amount, reason }` | Issued only by the season-close process manager. |
| `CloseAccount` *(planned)* | `AccountClosed` or `BalanceWrittenOff` | Balance must be zero, or the remainder is explicitly written off. |

**Balance** = assessed − confirmed ± adjustments − payouts. Derived on read; never stored.

### `Season` — one per league × label

| Command | Event | Invariant / behaviour |
|---|---|---|
| `OpenSeason` | `SeasonOpened { label, duesAmount, dueDate, openedBy }` | Label required. Amount > 0. Due date required. Already open (by id) → no event (idempotent). |
| `SetPayoutStructure` *(planned)* | `PayoutStructureSet { places: [{ rank, share }] }` — shares sum to 100% |
| `DeclareStandings` *(planned)* | `StandingsDeclared { rank → memberId }` |
| `CloseSeason` *(planned)* | `SeasonClosed` |

Opening a season also opens an `AccountOpened` + `DuesAssessed` for every current member of the league,
each on that member's own `MemberAccount` stream, all in the transaction that writes `SeasonOpened`.

**Deterministic ids** (`SeasonIds`, base-class-library only — a hand-rolled name-based UUID, since the
BCL has no version-5 `Guid` factory): the season id is computed from the league id and label; the
account id from the season id and member id; the season-dues assessment id from the season id alone,
shared by every member's line. The same inputs always compute the same ids, so opening a season twice
— a retry, or a second treasurer's click — lands on the same streams and adds no events, without a
lookup first.

### The rule that spans streams

Total payouts cannot exceed the confirmed pot. The pot is the sum over every `MemberAccount` in the
season, so no single stream can enforce it. A **process manager** reacting to `SeasonClosed` reads
the `LeaguePot` projection, computes payouts from the structure and standings, and issues
`RecordPayout` to each account. Eventually consistent, deliberately —
[ADR-0003](adr/0003-aggregate-boundaries-and-payout-process-manager.md) covers the alternatives.

## Projections (read side)

| Projection | Kind | Serves |
|---|---|---|
| `MemberStatement` | single-stream, inline | The member page: assessments for now (memo, due date, who, when); attestations and payouts join it later. |
| `SeasonListing` | single-stream, inline | `GET …/seasons`: a season's label, dues and due date, without replaying its stream. |
| `LeaguePot` *(planned)* | multi-stream, async | Treasurer dashboard: pot, confirmed vs outstanding, delinquency. |
| `ConfirmationQueue` *(planned)* | multi-stream, inline | The treasurer's "needs a decision" list. |
| History | none — the stream itself | `GET …/history`: events with timestamps, causation/correlation ids and the acting user. |

`Season` and `MemberAccount` themselves stay private-set domain aggregates, rebuilt live from raw
events (never stored) so a command can decide against current state; `MemberStatement` and
`SeasonListing` are separate, plain read models with public setters, because Marten's document
serializer needs to round-trip them from storage.

One projection is async on purpose: it exercises the projection daemon and gives a measurable
**projection lag** under scale-to-zero ([ADR-0006](adr/0006-inline-vs-async-projections.md)).

## Idempotency and concurrency

- Commands carry the id of the fact they create (`assessmentId`, `attestationId`). Replays converge
  inside the aggregate.
- Every mutating HTTP request will carry an `Idempotency-Key`; the stored response is returned on a
  retry. Webhooks use the provider's event id as the key.
- Commands carry the expected stream `Version`; a stale version is a `409` with the current version.
  Two treasurers confirming the same attestation at once produce exactly one `PaymentConfirmed`.
- Events and outgoing messages are committed in one transaction (Wolverine's outbox on the Marten
  session). No dual writes.
- A `…By` field on an event holds the acting *member id* (from `UserMemberships`/the league itself).
  `OpenSeasonEndpoint` additionally sets the caller's raw sign-in subject as a Marten event header
  (`EventHeaders.Subject`), so the history keeps it without widening the event body; later handlers
  should reuse the same header key.

## Event evolution

Events are never edited. A change in shape is a new event type (`PaymentAttestedV2`) with an
upcaster from the old one. Deleting or renaming a released event type is not allowed.
