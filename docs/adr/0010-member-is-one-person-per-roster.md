# ADR-0010: A member is one person, imported one per Sleeper roster

- **Status:** Accepted
- **Date:** 2026-09-24

## Context

`MemberAccount` is keyed by member, so whatever a member *is* decides whose books exist and who owes
dues. Sleeper rosters can have a primary owner and co-owners, or no owner at all. An import has to
map each roster to something BallBank can bill.

## Decision

A **member** is the one person responsible for a team's dues. Import creates exactly one member per
roster, taken from the roster's owner; co-owners are ignored. A roster with no owner still yields an
unclaimed member, because the team still owes dues. A member is held by at most one identity, and
an identity holds at most one member per league.

When a different person takes over a team mid-season, the treasurer revokes the old claim and issues
a new invite: the member, its account and its history follow the team to the new person. If the
newcomer should not inherit a debt, the treasurer posts an adjustment.

Roles (Treasurer) attach to the member. There is one role; a second appointee is another treasurer.

## Alternatives considered

- **Member as a team seat held by one or more identities.** Handles co-owned teams faithfully, but
  co-owners then share treasurer powers, and every claim, invite and authorization rule gets a
  "which holder" dimension. Rejected: dues are owed by one person in practice, and the league does
  not care how Sleeper splits ownership.
- **Member per person, co-owners each imported.** Two sets of books for one team's dues. Rejected.
- **Separate Treasurer and co-treasurer roles.** No scenario asked for different powers. Rejected.

## Consequences

Co-owned teams show one name. Taking over a team is two explicit facts in history (revoked, then
claimed), never one silent replacement. Roster-change events (`MemberOwnerChanged`,
`MemberRemoved`) are reserved names, not built in M1.
