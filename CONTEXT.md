# BallBank — domain context

The words this codebase uses, with the meaning it gives them. Code, tests, issues and docs use these
terms and avoid the synonyms listed. Maintained by `/domain-modeling`; edit when a term is resolved,
not before.

## Contexts

| Context | Owns | Lives in |
|---|---|---|
| **Membership** | Leagues, members, identities, claims, invites, roles, the Sleeper import | `src/BallBank.Domain/Membership`, `src/BallBank.Api/Features/Membership` |
| **Treasury** | Assessments, attestations, confirmations, adjustments, payouts, season close | `src/BallBank.Domain/Treasury`, `src/BallBank.Api/Features/Treasury` |
| **Payment Rails** | How a payment is said to have moved; adapters that verify or demo it | `src/BallBank.Api/Integrations` (planned) |
| **Notifications** | What BallBank tells a league's channels, and what a member agreed to be told: the season opening and confirmed payments over Discord; a member's consent and quiet hours for text messages; reminders, digests and sending SMS to come | `src/BallBank.Domain/Notifications`, `src/BallBank.Api/Features/Notifications`, `src/BallBank.Api/Integrations/Discord` |

## Glossary

**League** — A group of members who play together. The unit of tenancy: every event and document
carries the league as its tenant. Imported from Sleeper, but BallBank's league outlives Sleeper's
yearly `league_id`. Not: "group", "pool", "tenant" (in domain code).

**Season** — One year of a league. Dues, payout structure and standings belong to a season. Not: "year".

**Member** — The one person responsible for a team's dues in a league. Imported from a Sleeper
roster, one member per roster, taken from the roster's owner; Sleeper co-owners are ignored. A
roster with no owner still yields a member, because the team still owes dues. A member is *claimed*
once an identity has claimed it, otherwise *unclaimed*. Not: "user", "player", "owner", "manager",
"team" (that is what Sleeper calls it), "roster" (that is Sleeper's record of it), "seat".

**Identity** — A signed-in person, known to BallBank only by the subject their sign-in provider
issues. An identity becomes a member by claiming one; one identity can be a member of many
leagues, but at most one member per league, and a member is held by at most one identity. Not:
"user", "account", "login".

**Claim** — The act, and the resulting fact, of an identity becoming a member through an invite.
The person who imports a league claims their own member in the same step. Not: "link",
"registration", "join".

**Invite** — A single-use, expiring link the treasurer issues for one specific member so the right
person can claim it. Opening it while signed in completes the claim; no approval step. Not:
"invitation code", "join link".

**Revoke** — A treasurer's act of ending an identity's claim to a member: the wrong person claimed
it, or its owner left. The member keeps its account and history for whoever claims it next; only its
claimant changes (ADR-0010). A league always keeps at least one treasurer, so revoking the only
treasurer's claim is refused. Not: "unclaim", "remove", "kick".

**Contact details** — How to reach a member: email, phone number, Discord username. They belong
to the member within a league, so a treasurer can record a phone number for a member who has not
claimed yet and text them their invite. A person in two leagues has contact details in each.
Not: "profile", "settings".

**Consent** — A member's agreement to be texted, given by them alone (never by a treasurer), at one
specific number, and recorded with when they gave it. It belongs to the number: a different number
has no consent until the member opts in at it, and consent goes with the claim it was given under.
A member *may be texted* while consent stands at the number on record and that number has not
opted out. An **opt-out** is a number's, not a member's: the STOP a number replied with, which binds
every league it is in and outranks consent. A member turning texts off in the app withdraws consent,
which is not an opt-out. Not: "subscription", "opt-in flag", "permission".

**Quiet hours** — The hours a member asks not to be texted, by their own clock: a start hour, an end
hour and an IANA time zone, nine at night to nine in the morning Eastern until they choose others.
A message that falls inside them is held until they end, not dropped. Not: "do not disturb",
"snooze".

**Treasurer** — A member who keeps the books for a league: assesses dues, confirms and rejects
payments, posts adjustments, declares standings, closes the season. A role BallBank assigns to a
member. The member who imports the league becomes a Treasurer; any treasurer can appoint another.
There is one role; a second appointee is simply another treasurer. Sleeper's `is_owner` (its commissioner flag) is only a suggestion of whom to appoint. Not:
"commissioner", "admin", "co-treasurer".

**Import** — Bringing a Sleeper league into BallBank: the league, its season, and one member per
roster. A treasurer can import the same Sleeper league into the same BallBank league again: rosters
that appeared since become members, matched by Sleeper roster id, and everything already known is
left alone. That is still an import. One Sleeper league can back only one BallBank league. Not:
"sync", "refresh", "pull".

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

**Confirmation queue** — Every pending attestation of a season, oldest first, for a treasurer to
confirm or reject. Only treasurers see it. A view of the statements, not a thing of its own. Not:
"inbox", "approvals", "to-do".

**Adjustment** — A treasurer-posted correction with a reason: a waiver, a refund of an overpayment,
a write-off. Not: "credit"/"debit" as nouns.

**Rail** (`PaymentRail`) — How the money is said to have moved: Cash, Venmo, Zelle, PayPal, CashApp,
Check, Other. BallBank never moves money itself. Not: "method", "provider", "processor".

**Reference** — The identifier a member gives with an attestation so the treasurer can find the
payment (Venmo note, Zelle confirmation, check number). Required for every rail but Cash.

**Balance** — Derived: assessed − confirmed ± adjustments − payouts. Never stored. Positive means
the member owes the pot. Not: "amount due", "outstanding" (that is the league-wide total of the
positive balances; the total of the negative ones, what the pot owes back, is "owed").

**Pot** — The sum of confirmed payments across a season's accounts, less refunds. What payouts are
drawn from. Other adjustments move no money, so they leave the pot alone. Not: "prize pool", "bank".

**Refund** — An adjustment that records money the treasurer paid a member back out of the pot, as
after an overpayment. Raises the balance and lowers the pot by the same amount, and can be no more than
the pot owes the member. Not: "reimbursement",
"chargeback".

**Payout** — Money the pot returns to a member at season close, per the payout structure and the
declared standings. Recorded on the member's account. Not: "prize", "winnings".

**Payout structure** — How the pot is split by finishing place (e.g. 60/30/10). Shares sum to 100%.

**Standings** — Final finishing places for a season, taken from Sleeper's records or entered by the
treasurer, then declared as a fact. Not: "results", "bracket".

**Statement** — The read model of one account: balance, line items, pending attestations. A
projection, rebuildable from events. Not: "invoice", "bill".

**Ledger** — Every account of one season of a league, together: each member, their balance and how
many of their attestations are pending. Every member of the league sees it; only a treasurer, or
the account's own member, sees the statement behind a line. Not: "books" (the league keeps its books
*in* BallBank; the ledger is one season's view of them), "leaderboard", "outstanding" (that is a
number).

**Dashboard** — A treasurer's view of one season's totals: the pot, what members still owe (outstanding),
what the pot owes members (owed), the attestations still pending, and the delinquents. Only
treasurers see it. It trails the events by a moment and says when it was last updated ("as of").
A projection, rebuildable from events. Not: "report", "overview", "summary".

**Delinquent** — A member whose balance is still above zero after the earliest due date of what
they were assessed. The days overdue count from that date; on the due date itself nobody is
delinquent yet. Not: "late" or "overdue" as a label for the member (those describe how far past
the date they are), "outstanding" (that is the league-wide total).

**Notification** — One thing BallBank decided to tell someone, because something happened: a season
opened, a payment was confirmed. It is decided in reaction to a Treasury event and recorded with a dedupe
key (channel, recipient, kind, cause), so the same cause is never told twice on the same channel, however many
times its event is handled. It is delivered after the fact, so a channel that is down never holds up the
books. *Pending* until its channel takes it, *sent* once it has, *dropped* if the league disconnected the
channel first. What is said is worded in the domain and rendered once, when the event is handled.
Attesting and rejecting a payment are never announced. Not: "alert", "message" (that is what Wolverine
calls the thing that carries it), "event" (that is the fact that caused it).

**Channel** — Where a notification goes: Discord today, SMS to come. A league connects its Discord
channel once, with a webhook the treasurer pastes in, and chooses what is announced there. The webhook is
a secret, since whoever holds it can post to the channel, so no response repeats it whole: only whether
it is connected and its last few characters. Disconnecting forgets it and everything waiting to be sent.
Not: "integration", "webhook" (that is Discord's word for the address of one).

**History** — The event stream itself, with who and when on every entry. The audit trail is not a
separate thing. Not: "log", "audit table".

## Rules the glossary implies

- A balance is always computed from events; nothing stores a running total.
- An attestation never changes a balance; only a confirmation does.
- Every command carries the id of the fact it wants to create, so retries converge.
- The tenant (league) comes from the request's route and token, never from its body.
- Roles belong to members. A member is one person, so a role is one person's.
- A member's books follow the team: if a different person takes over the team, the treasurer
  moves the member to that person; the account and its history stay put.
