# Domain model

Vocabulary is defined in [CONTEXT.md](../CONTEXT.md). This page is the shape: which streams exist,
which events they hold, and which invariants each command enforces.

## Streams

### `MemberAccount` — one per league × season × member

Where the money invariants live.

| Command | Event | Invariant / behaviour |
|---|---|---|
| `OpenAccount` | `AccountOpened` | One per member per season. Needs a season. |
| `AssessDues` | `DuesAssessed { assessmentId, amount, dueDate, memo }` | Id required. Amount > 0, in whole cents. Due date required. Same `assessmentId` again → no event (idempotent). |
| `AttestPayment` | `PaymentAttested { attestationId, amount, rail, reference }` | Amount > 0. Reference required unless the rail is Cash. Same `attestationId` again → no event. Does **not** change the balance. |
| `ConfirmPayment` | `PaymentConfirmed { attestationId }` | Attestation must exist and be pending. Already confirmed → no event. Rejected → refused. Moves the balance. |
| `RejectPayment` | `PaymentRejected { attestationId, reason }` | Reason required. Already rejected → no event. Confirmed → refused (post an adjustment instead). |
| `PostAdjustment` | `AdjustmentPosted { adjustmentId, amount, reason, refund, postedBy }` | Id required. Reason required. Amount signed (positive raises the balance, negative lowers it), not zero, in whole cents. A refund must be positive, cannot be more than the pot owes the member (the negative of the balance), and is the only adjustment that lowers the pot. Same `adjustmentId` again → no event. Treasurer only (enforced at the endpoint, which knows the league's roles). |
| `RecordPayout` *(planned)* | `PayoutRecorded { amount, reason }` | Issued only by the season-close process manager. |
| `CloseAccount` *(planned)* | `AccountClosed` or `BalanceWrittenOff` | Balance must be zero, or the remainder is explicitly written off. |

**Balance** = assessed − confirmed + adjusted − payouts, where adjusted is the signed sum of the
adjustments. Derived on read; never stored.

### `Season` — one per league × label

| Command | Event | Invariant / behaviour |
|---|---|---|
| `OpenSeason` | `SeasonOpened { label, duesAmount, dueDate, openedBy }` | Label required. Amount > 0. Due date required. Already open (by id) → no event (idempotent). |
| `SetPayoutStructure` *(planned)* | `PayoutStructureSet { places: [{ rank, share }] }` — shares sum to 100% |
| `DeclareStandings` *(planned)* | `StandingsDeclared { rank → memberId }` |
| `CloseSeason` *(planned)* | `SeasonClosed` |

Opening a season also opens an `AccountOpened` + `DuesAssessed` for every current member of the league,
each on that member's own `MemberAccount` stream, all in the transaction that writes `SeasonOpened`.

Assessing a late fee or a side-pot buy-in (`POST …/seasons/{season}/assessments`) appends `DuesAssessed`
with one assessment id to every targeted account (every member, or the members named) in one
transaction. The id is shared on purpose: one assessment applied to many books, unique within each
stream, so an account that already carries it is skipped. A member added since the season opened has
no account yet; their first assessment opens it (`AccountOpened` + `DuesAssessed`) in the same
transaction. No expected version, since it spans many streams: an account appended to meanwhile
fails the whole transaction, and assessing again converges.

**Deterministic ids** (`SeasonIds`, base-class-library only — a hand-rolled name-based UUID, since the
BCL has no version-5 `Guid` factory): the season id is computed from the league id and label; the
account id from the season id and member id; the season-dues assessment id from the season id alone,
shared by every member's line. The same inputs always compute the same ids, so opening a season twice
— a retry, or a second treasurer's click — lands on the same streams and adds no events, without a
lookup first.

### The rule that spans streams

Total payouts cannot exceed the pot. The pot is the sum over every `MemberAccount` in the season of
its confirmed payments less its refunds (`MemberAccount.InThePot`), so no single stream can enforce it. A **process manager** reacting to `SeasonClosed` reads
the `LeaguePot` projection, computes payouts from the structure and standings, and issues
`RecordPayout` to each account. Eventually consistent, deliberately —
[ADR-0003](adr/0003-aggregate-boundaries-and-payout-process-manager.md) covers the alternatives.

## Projections (read side)

| Projection | Kind | Serves |
|---|---|---|
| `MemberStatement` | single-stream, inline | `GET …/accounts/{accountId}`: line items in order — assessments (memo, due date), attestations (rail, reference, status, rejection reason) and adjustments (signed amount, reason), each with who and when; payouts join it later. Totals and the balance are computed from the lines when served, never stored. |
| Ledger | none — a query over `MemberStatement` | `GET …/seasons/{season}/ledger`: every account of the season with its computed balance, pending count and version, named from the league's member list. |
| `SeasonListing` | single-stream, inline | `GET …/seasons`: a season's label, dues and due date, without replaying its stream. |
| `LeaguePot` *(planned)* | multi-stream, async | Treasurer dashboard: pot, confirmed vs outstanding, delinquency. |
| Confirmation queue | none — a query over `MemberStatement` | `GET …/seasons/{season}/confirmations` (treasurers): every pending attestation of the season, oldest first, with member, amount, rail, reference, who attested and when, and the account's version ([ADR-0006](adr/0006-inline-vs-async-projections.md)). |
| History | none — the stream itself | `GET …/accounts/{accountId}/history`: one entry per event of the account, in order — sequence, version, when, event type, the acting member and their name, and a sentence the API renders in league language. An account's opener is whoever assessed its first dues, in the same step. Causation/correlation ids join it later. |

`Season` and `MemberAccount` themselves stay private-set domain aggregates, rebuilt live from raw
events (never stored) so a command can decide against current state; `MemberStatement` and
`SeasonListing` are separate, plain read models with public setters, because Marten's document
serializer needs to round-trip them from storage.

One projection is async on purpose: it exercises the projection daemon and gives a measurable
**projection lag** under scale-to-zero ([ADR-0006](adr/0006-inline-vs-async-projections.md)).

## Idempotency and concurrency

- Commands carry the id of the fact they create (`assessmentId`, `attestationId`). Replays converge
  inside the aggregate.
- Every mutating HTTP request carries an `Idempotency-Key` (`412` without one); the stored response
  is returned on a retry, and the same key for a different request is a `422`. The answer is an
  `IdempotencyRecord`, scoped to league and caller, written in the same transaction as the events.
  Opening a season is the first endpoint to require it; later mutating endpoints adopt it
  ([ADR-0005](adr/0005-idempotency-and-concurrency.md)). Webhooks use the provider's event id as the key.
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
