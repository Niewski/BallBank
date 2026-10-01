# ADR-0011: Three sanctioned cross-tenant documents (amends ADR-0004)

- **Status:** Accepted
- **Date:** 2026-09-24 (amended 2026-09-29 for `PhoneOptOut`; its writer, the Twilio webhook, 2026-10-01)

## Context

ADR-0004 makes every event and document tenant-scoped by league and requires cross-tenant access to
be explicit and audited. Four lookups cannot be answered inside one tenant: "my leagues" for a
signed-in identity, the membership check that gates every `/leagues/{leagueId}` request, the
guard that one Sleeper league backs only one BallBank league, and whether a phone number has
replied STOP, which a text message carries no league to say and which binds every league the
number is in.

## Decision

Three documents are exempt from `AllDocumentsAreMultiTenanted()` and live in the default tenant:

- `UserMemberships`, keyed by sign-in subject: the leagues the identity is a member of, with the
  member id and roles in each. Written in the same transaction as the claim, appointment or
  revocation event that changes it.
- `SleeperLeagueIndex`, keyed by Sleeper league id: the BallBank league it backs. Written in the
  same transaction as `LeagueImported`.
- `PhoneOptOut`, keyed by the number in E.164: that it replied STOP, and when. Read by every league
  that asks whether a number may be texted. Written by the Twilio inbound webhook
  (`POST /webhooks/twilio/inbound`), which knows only the number: STOP creates it, keeping the first
  time if there is one already, and START deletes it. A signed request is the only way either happens.

Authorization reads `UserMemberships` as its fast gate; the league stream remains the source of
truth and re-checks the role on every treasurer command.

## Alternatives considered

- **`QueryAllTenants` at request time.** Turns the hot path of every request into a cross-tenant
  scan. Rejected.
- **League ids and roles in the JWT via an Auth0 action.** State in a token goes stale on every
  claim and appointment. Rejected.
- **A separate global database.** Two databases for two small documents. Rejected.

## Consequences

Any new cross-tenant document must be added to this ADR. `UserMemberships` and `SleeperLeagueIndex`
are derived from events and can be rebuilt; they are never the only record of a membership.
`PhoneOptOut` is not derived from anything in the league: it is the only record that a number said
STOP, so it is kept, never rebuilt, and holds nothing but the number and the time. It is removed only
by the number's own START; a member's consent in a league is a separate fact, so after START texts
resume where consent is still on record, and nowhere else.
