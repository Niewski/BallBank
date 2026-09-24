# ADR-0002: Ledger of record, no custody of funds

- **Status:** Accepted
- **Date:** 2026-09-24

## Context

A league's dues are real money, and a product that collects and disburses other people's money
becomes a financial intermediary. Holding pooled funds raises money-transmission questions that vary
by state, and payment processors restrict pooled prize money for fantasy sports. None of that is the
problem BallBank exists to solve, which is that nobody can remember who paid.

Members already pay each other through rails they trust: Venmo, Zelle, cash, a check at the draft.

## Decision

BallBank is a **ledger of record**. It never holds, moves or processes money.

- A member records an **attestation**: "I paid $50 by Venmo, reference VN-1234."
- The treasurer **confirms** it (after seeing the money arrive on the rail) or **rejects** it with a reason.
- Only a confirmation changes the balance. The stream is the audit trail.

Payment rails are a port (`IPaymentRail`). The production adapter is manual attestation. A Stripe
adapter exists only in test mode, to demonstrate idempotent webhook handling and a payment state
machine, and is never enabled for a real league.

## Alternatives considered

- **Collect dues by card through a processor and disburse payouts.** Real integration, but BallBank
  becomes the custodian of the pot, with the legal and policy exposure that implies, plus fees that
  would exceed the rest of the system's running cost.
- **Payment links to the treasurer's own account, reconciled automatically.** Still runs the league's
  money through a processor under fantasy-pool restrictions, for little gain over attestation.

## Consequences

- The treasurer remains the human in the loop for every confirmation. The confirmation queue and
  reminders exist to make that cheap.
- Disputes are visible: an attestation, a rejection with a reason, and a re-attestation are all events.
- Verifying a payment automatically (for example by matching a Venmo export) is possible later as a
  new rail adapter without touching the domain.
- The public deployment must state plainly that it does not process payments.
