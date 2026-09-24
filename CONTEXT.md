# BallBank — domain context

The words this codebase uses, with the meaning it gives them. Code, tests, issues and docs use these
terms and avoid the synonyms listed. Maintained by `/domain-modeling`; edit when a term is resolved,
not before.

## Contexts

| Context | Owns | Lives in |
|---|---|---|
| **Membership** | Leagues, seasons, members, roles, the Sleeper import | `src/BallBank.Api/Features/Membership` (planned) |
| **Treasury** | Assessments, attestations, confirmations, adjustments, payouts, season close | `src/BallBank.Domain/Treasury`, `src/BallBank.Api/Features/Treasury` |
| **Payment Rails** | How a payment is said to have moved; adapters that verify or demo it | `src/BallBank.Api/Integrations` (planned) |
| **Notifications** | Reminders and digests over Discord and SMS | `src/BallBank.Api/Features/Notifications` (planned) |

## Glossary

**League** — A group of members who play together. The unit of tenancy: every event and document
carries the league as its tenant. Imported from Sleeper, but BallBank's league outlives Sleeper's
yearly `league_id`. Not: "group", "pool", "tenant" (in domain code).

**Season** — One year of a league. Dues, payout structure and standings belong to a season. Not: "year".

**Member** — A person in a league. Has a claimed identity (they signed in and picked their team) or
an unclaimed one (imported from Sleeper, no login yet). Not: "user", "player", "owner", "manager".

**Treasurer** — The member who keeps the books for a league: assesses dues, confirms and rejects
payments, posts adjustments, declares standings, closes the season. A role BallBank assigns; the
person who imports the league becomes Treasurer. Sleeper's `is_owner` (its commissioner flag) is
only an offer to become a co-treasurer. Not: "commissioner", "admin".

**Account** (`MemberAccount`) — One member's books for one season of one league. An event stream.
Not: "wallet", "ledger" (that is the whole of them together), "balance" (that is a number).

**Assessment** — An amount a member owes, recorded by the treasurer: season dues, a late fee, a
side-pot buy-in. Each has its own id so re-issuing it is a no-op. Not: "charge", "invoice", "fee"
(a fee is one kind of assessment).

**Attestation** — A member's claim that they paid: amount, rail, reference. Pending until the
treasurer confirms or rejects it. Does not change the balance. Not: "payment" (a payment is what
happened outside BallBank), "transaction".

**Confirmation** — The treasurer's acknowledgement that an attested payment arrived. This is the
moment the balance moves. Not: "approval", "verification".

**Rejection** — The treasurer could not find or did not accept an attested payment. Always carries a
reason the member sees. Not: "denial".

**Adjustment** — A treasurer-posted correction with a reason: a waiver, a refund of an overpayment,
a write-off. Not: "credit"/"debit" as nouns.

**Rail** (`PaymentRail`) — How the money is said to have moved: Cash, Venmo, Zelle, PayPal, CashApp,
Check, Other. BallBank never moves money itself. Not: "method", "provider", "processor".

**Reference** — The identifier a member gives with an attestation so the treasurer can find the
payment (Venmo note, Zelle confirmation, check number). Required for every rail but Cash.

**Balance** — Derived: assessed − confirmed ± adjustments − payouts. Never stored. Positive means
the member owes the pot. Not: "amount due", "outstanding" (that is the league-wide total).

**Pot** — The sum of confirmed payments across a season's accounts. What payouts are drawn from.
Not: "prize pool", "bank".

**Payout** — Money the pot returns to a member at season close, per the payout structure and the
declared standings. Recorded on the member's account. Not: "prize", "winnings".

**Payout structure** — How the pot is split by finishing place (e.g. 60/30/10). Shares sum to 100%.

**Standings** — Final finishing places for a season, taken from Sleeper's records or entered by the
treasurer, then declared as a fact. Not: "results", "bracket".

**Statement** — The read model of one account: balance, line items, pending attestations. A
projection, rebuildable from events. Not: "invoice", "bill".

**History** — The event stream itself, with who and when on every entry. The audit trail is not a
separate thing. Not: "log", "audit table".

## Rules the glossary implies

- A balance is always computed from events; nothing stores a running total.
- An attestation never changes a balance; only a confirmation does.
- Every command carries the id of the fact it wants to create, so retries converge.
- The tenant (league) comes from the request's route and token, never from its body.
