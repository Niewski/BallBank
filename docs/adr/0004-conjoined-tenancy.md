# ADR-0004: Conjoined tenancy, tenant = league

- **Status:** Proposed, amended by ADR-0011 (two cross-tenant documents)
- **Date:** 2026-09-24

## Context

Many leagues will share one database. Each league's data must be invisible to the others, and the
blast radius of a bug must be contained, at a cost of roughly zero.

## Decision (proposed)

Marten conjoined tenancy: `tenant_id` on every event and document row; sessions opened per tenant.
The tenant is derived server-side from the route (`/leagues/{leagueId}/…`) after authorisation
confirms membership — never from the request body. Row-level security as defence in depth.
Database-per-tenant remains available as a higher isolation tier, confined to store configuration.

## Alternatives

- **Schema or database per league.** Stronger isolation, more moving parts; unnecessary at this size.
- **Application-level filtering only.** One missed `where` clause leaks a league's books.

## Consequences

Every query is tenant-scoped by construction. Cross-tenant operations (support tooling, global
metrics) must be explicit and audited.
