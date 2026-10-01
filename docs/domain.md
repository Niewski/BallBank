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
| `LeaguePot` | multi-stream, async (projection daemon) | `GET …/seasons/{season}/dashboard` (treasurers): one document per season, built from every account of it — assessed, confirmed, refunded, the pot, what members still owe (outstanding) and what the pot owes them (owed), the pending attestations, and the delinquents with team, balance and days overdue — with when the last event it reflects was recorded. Sums are kept per account; the figures are computed from them when served ([ADR-0006](adr/0006-inline-vs-async-projections.md)). |
| Confirmation queue | none — a query over `MemberStatement` | `GET …/seasons/{season}/confirmations` (treasurers): every pending attestation of the season, oldest first, with member, amount, rail, reference, who attested and when, and the account's version ([ADR-0006](adr/0006-inline-vs-async-projections.md)). |
| History | none — the stream itself | `GET …/accounts/{accountId}/history`: one entry per event of the account, in order — sequence, version, when, event type, the acting member and their name, and a sentence the API renders in league language. An account's opener is whoever assessed its first dues, in the same step. Causation/correlation ids join it later. |

`Season` and `MemberAccount` themselves stay private-set domain aggregates, rebuilt live from raw
events (never stored) so a command can decide against current state; `MemberStatement` and
`SeasonListing` are separate, plain read models with public setters, because Marten's document
serializer needs to round-trip them from storage.

One projection is async on purpose: it exercises the projection daemon and gives a measurable
**projection lag** under scale-to-zero ([ADR-0006](adr/0006-inline-vs-async-projections.md)).

## Notifications

Two tenant-scoped Marten documents, in the API (`Features/Notifications`); the words a notification
says are in the package-free `BallBank.Domain.Notifications` (`NotificationTexts`, with domain tests).
What a member has agreed to be texted is a third document, `NotificationPreferences`, below.

| Document | Keyed by | Holds |
|---|---|---|
| `LeagueNotificationSettings` | the league id | The Discord webhook URL, `AnnouncePayments`, `PostDigest` (stored and toggled, but nothing posts the digest yet). Written straight by `PUT …/notifications/discord`, with no event ([ADR-0012](adr/0012-contact-details-are-a-document-not-events.md)); `DELETE` removes it, webhook and flags together, so it exists only while Discord is connected. The webhook is stored as it was accepted (a Discord host, path `/api/webhooks/{id}/{token}`) and is never returned: `GET` answers connected or not, the last four characters, and the flags. Treasurers only. |
| `Notification` | its dedupe key, `{channel}/{recipient}/{kind}/{cause}` (the league is the recipient of a Discord post, a member of a text) | `Kind`, `Channel`, `MemberId` (for a text), the rendered `Text`, `Status` (`Pending`, `Sent`, `Dropped`, `Skipped`, `Held`), `Reason` (why `Skipped`), `SendAfter` (when a `Held` one may go), `CreatedAt`, `SentAt`. Inserting it claims the key, so an event handled twice inserts twice, the second insert fails, and nothing more is sent. |

| Event handled | Told, when | Cause in the key |
|---|---|---|
| `SeasonOpened` | Discord is connected: the league's name, the season, the dues and when they are due. Texts nobody: each member hears of the dues assessed to them. | the season id |
| `PaymentConfirmed` | Discord is connected **and** `AnnouncePayments` is on: the payer's team name, the amount, and what the pot holds when the event is handled. By text: the member whose account it is, unless they confirmed it themselves. | the attestation id |
| `DuesAssessed` | By text, the member assessed: the amount, what it is for and when it is due. | the assessment id |
| `PaymentAttested` | By text, every treasurer but the one who attested: who says they paid, how much, and by which rail. | the attestation id |
| `PaymentRejected` | By text, the member: the amount, the rail and the reason. | the attestation id |
| `AdjustmentPosted` | By text, the member: how their balance moved and why, unless they posted it themselves. | the adjustment id |

`PaymentAttested` and `PaymentRejected` are never announced on Discord. The pot in a payment's text is
the accounts' figures at handling time, so a second confirmation handled first shows the larger pot.
Connecting posts a hello through the webhook in the request itself; Discord refusing it is a `400` and
nothing is saved.

Marten forwards these Treasury events to Wolverine in the transaction that commits them, but only
for sessions opened through Wolverine's `OutboxedSessionFactory`: a plain `LightweightSession` with an
enrolled outbox appends the events and forwards nothing. The endpoints that append them
(`OpenSeasonEndpoint`, `AssessEndpoint`, `AttestPaymentEndpoint`, the confirmation and rejection
endpoints, `PostAdjustmentEndpoint`) open their session that way. One handler hears each event,
records the `Notification`(s) and returns a `SendNotification` for each, which goes to the durable
local queue `notifications`. Sending marks the notification `Sent`. A send that fails is retried on a
schedule (`Notifications:RetryDelays`, by default 5 s, 30 s and 5 min) and then dead-lettered; the
notification stays `Pending`, and the confirmation that caused it was never held up
([runbook](runbook.md#replay-a-dead-lettered-message)).

### Texts

Every text is worded in the domain (`SmsTexts`, with domain tests): it names BallBank and the league, and
ends with the link to the member's statement, `/statement?league=&account=` on `Notifications:WebBaseUrl`
(or, for a treasurer told of an attestation, to the account that attested).

Whether a member is texted is decided when the text is about to go, not when the event is handled,
from what the member has said by then (`SmsDelivery.Decide`, with domain tests): a number that
replied STOP (`PhoneOptOut`) is **skipped** (`Reason` says why), then a member with no consent at the
number now on record is **skipped**, then a message inside the member's quiet hours is **held** with
`SendAfter` at the end of the hours. Otherwise it goes. A held notification is sent by sending it again
once its time has come; the tick that does that is not built yet, so nothing is scheduled and a held
text waits. A skipped one never goes.

`TwilioSmsChannel` is the second `INotificationChannel`: a typed `HttpClient` under the resilience
handler and a rate limit in front of it, posting to Twilio's Messages API from the deployment's one
toll-free number (`Twilio:FromNumber`), with `StatusCallback` set to
`POST /webhooks/twilio/status?league=&notification=` on the API (`Twilio:StatusCallbackBaseUrl`); the
endpoint that receives it, and so a text's delivery status, is not built yet. Neither the auth token
nor a member's number is logged, traced or put in an exception. A machine without Twilio settings
fails every send, which ends in the dead-letter queue like any other.

### `NotificationPreferences` — one per member, tenant-scoped

What a member has said about being texted, kept beside their `MemberContact`
([ADR-0012](adr/0012-contact-details-are-a-document-not-events.md)): the same reasons hold. It is
replaced whole by `PUT …/members/{memberId}/notifications` (`204`), with no `Idempotency-Key` or
version, and read by `GET` (a treasurer, or the member themselves).

- **Consent** — the number (E.164) the member opted in at, and when. Consent belongs to the number:
  saving contact details with a different number clears it, and consent at one number is not consent
  at another. Only the member gives it (`403` to a treasurer acting for them), and only for a number
  on record (`409` with none). Opting in again at the same number keeps the original time. Revoking
  a claim deletes the document, quiet hours too: they were the revoked person's, and whoever claims
  the member next chooses their own.
- **Quiet hours** — a start hour, an end hour and an IANA time zone, `21` to `9` in
  `America/New_York` until the member chooses others. The window may cross midnight. A message that
  falls inside it is held until it ends, not dropped: `QuietHours.HeldUntil(instant)` answers when,
  reading the hours on the member's own clock across a change to daylight saving time (a wall-clock
  hour that does not exist that day begins at the first instant that does; one that happens twice is
  the earlier). Bad hours or an unknown zone are `400`.
- **May be texted** — `SmsConsent.PermitsTexting(consent, phone, numberOptedOut)`: consent at the number
  now on record, and that number has not replied STOP. The reading and the members list
  (`textsOptedIn`, for a treasurer and for the member themselves) both answer it, so a client never
  repeats the rule.

`PhoneOptOut` is the STOP a number replied with, keyed by the number and shared by every league it
appears in, so it lives in the default tenant ([ADR-0011](adr/0011-cross-tenant-documents.md)). The
notification preferences only *read* it; recording a STOP (and a START) arrives with the Twilio
webhook.

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
