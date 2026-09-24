# ADR-0011: Two sanctioned cross-tenant documents (amends ADR-0004)

- **Status:** Accepted
- **Date:** 2026-09-24

## Context

ADR-0004 makes every event and document tenant-scoped by league and requires cross-tenant access to
be explicit and audited. Three lookups cannot be answered inside one tenant: "my leagues" for a
signed-in identity, the membership check that gates every `/leagues/{leagueId}` request, and the
guard that one Sleeper league backs only one BallBank league.

## Decision

Two documents are exempt from `AllDocumentsAreMultiTenanted()` and live in the default tenant:

- `UserMemberships`, keyed by sign-in subject: the leagues the identity is a member of, with the
  member id and roles in each. Written in the same transaction as the claim, appointment or
  revocation event that changes it.
- `SleeperLeagueIndex`, keyed by Sleeper league id: the BallBank league it backs. Written in the
  same transaction as `LeagueImported`.

Authorization reads `UserMemberships` as its fast gate; the league stream remains the source of
truth and re-checks the role on every treasurer command.

## Alternatives considered

- **`QueryAllTenants` at request time.** Turns the hot path of every request into a cross-tenant
  scan. Rejected.
- **League ids and roles in the JWT via an Auth0 action.** State in a token goes stale on every
  claim and appointment. Rejected.
- **A separate global database.** Two databases for two small documents. Rejected.

## Consequences

Any new cross-tenant document must be added to this ADR. Both documents are derived from events and
can be rebuilt; they are never the only record of a membership.
