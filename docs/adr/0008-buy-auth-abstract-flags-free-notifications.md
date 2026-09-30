# ADR-0008: Buy identity, abstract feature flags, free notification channels

- **Status:** Proposed; the Discord notification channel accepted 2026-09-29 (M3)
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

## As built: Discord announcements (M3)

- Marten forwards the Treasury events that have a handler to Wolverine in the transaction that commits
  them (`IntegrateWithWolverine`, fast event forwarding). This holds only for sessions opened by
  Wolverine's `OutboxedSessionFactory`; a `LightweightSession` with an enrolled outbox appends the
  events and forwards none, silently. Endpoints that append announced events open their session that way.
- One handler per event decides what is told and records a `Notification` document whose id is its dedupe
  key (channel, league, kind, cause). Inserting a key that is taken throws, the handler's transaction rolls
  back, and Wolverine discards the message: an event handled twice announces once.
- The handler returns a `SendNotification`, which runs on a durable local queue. Delivery is a separate
  step from the decision, so a failing channel retries on a schedule and then dead-letters without ever
  touching the request that committed the event.
- The webhook URL is a bearer secret. The typed HTTP client has no logger, the OpenTelemetry HTTP
  instrumentation skips `/api/webhooks/`, exceptions never carry the URL, messages set
  `allowed_mentions` to none, and only Discord's own hosts are accepted.
- Discord is faked at the HTTP message handler, as Sleeper is, so the adapter, its resilience handler and
  its error mapping all run in the tests.
