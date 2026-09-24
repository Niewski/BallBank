# ADR-0008: Buy identity, abstract feature flags, free notification channels

- **Status:** Proposed
- **Date:** 2026-09-24

## Context

Identity, feature flags and message delivery are not what BallBank is about, and each is a place
where a home-grown version becomes a liability.

## Decision (proposed)

- **Identity:** a managed provider (Auth0's free tier) issues JWTs; the API only validates them.
- **Feature flags:** code depends on the OpenFeature API; the provider starts as a static file and
  can become a hosted service without touching call sites.
- **Notifications:** Discord via webhook (free), SMS via Twilio behind `INotificationChannel`, with
  consent recorded per member.

## Consequences

Vendor dependencies are behind interfaces with a fake for tests. The only recurring third-party
cost is SMS.
