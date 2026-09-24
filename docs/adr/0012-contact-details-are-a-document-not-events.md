# ADR-0012: Contact details are a document, not events

- **Status:** Accepted
- **Date:** 2026-09-24

## Context

Treasurers need a member's email, phone number and Discord username to chase dues and to text an
invite to someone who has not signed in yet. This is the first personal data BallBank stores. Events
are immutable and replayed forever, so a phone number in an event can never be removed.

## Decision

Contact details live in a tenant-scoped Marten document, `MemberContact`, keyed by member. It is
written directly by the contact endpoint, with no event and no history. Nothing in the books
depends on it. Treasurers edit any member's; a claimed member edits their own; email is required
once a member is claimed, nothing is required before.

Contact details belong to the member within a league, not to the identity, so a treasurer can
record a number before the person has claimed. A person in two leagues enters them twice.

The treasurer sends invite texts from their own phone (`sms:` link with the invite prefilled).
BallBank does not send SMS until consent exists (M3).

## Alternatives considered

- **`MemberContactChanged` events projected to a document.** Full history, permanent personal data,
  no way to honour "delete my number". Rejected.
- **One contact document per identity, across leagues.** Entered once, but unusable for unclaimed
  members, which is the case that matters for invites. Rejected.
- **Sending invite SMS through Twilio in M1.** Pulls the paid channel and the consent problem
  forward for one convenience. Rejected.

## Consequences

Contact edits do not appear in history; a deliberate gap. Deleting a member's personal data is one
document write. Wherever notifications later fan out, they read `MemberContact` inside the tenant.
