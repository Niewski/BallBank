# ADR-0009: Azure Container Apps and Neon PostgreSQL

- **Status:** Proposed
- **Date:** 2026-09-24

## Context

The system must run for about nothing while serving one league, and grow without a rewrite.
Marten requires PostgreSQL.

## Decision (proposed)

- **API:** Azure Container Apps, consumption plan, scale-to-zero, one replica for now. The monthly
  free grant covers expected traffic; images are pulled from GitHub Container Registry.
- **Database:** Neon's free plan (0.5 GB, 100 compute-hours per project, suspends after 5 minutes
  idle). A season of one league is a few hundred events.
- **Web:** Azure Static Web Apps free plan for the static export.
- **Delivery:** GitHub Actions with OIDC federation to Azure; no stored credentials.

## Alternatives

- **App Service + Azure SQL.** Familiar, but Marten needs PostgreSQL and neither side scales to zero.
- **Fly.io / Railway / Render.** Fine platforms; the Azure free grant is larger for this shape of
  traffic and keeps one provider for compute, telemetry and static hosting.

## Consequences

Two cold starts in series (compute, then database) after idle; measured and published. A warm-up
tick before peak hours is a cheap mitigation.
