# ADR-0008: Buy identity, abstract feature flags, free notification channels

- **Status:** Notifications Accepted 2026-10-02 (M3); identity and feature flags Proposed
- **Date:** 2026-09-24

## Context

Identity, feature flags and message delivery are not what BallBank is about, and each is a place
where a home-grown version becomes a liability.

## Decision

- **Identity (proposed):** a managed provider (Auth0's free tier) issues JWTs; the API only validates them.
- **Feature flags (proposed):** code depends on the OpenFeature API; the provider starts as a static file and
  can become a hosted service without touching call sites.
- **Notifications (accepted):** Discord via webhook (free), SMS via Twilio, both behind
  `INotificationChannel`. Texts follow three rules, each decided when the text is about to go:
  - **Consent** is the member's own act (a treasurer cannot give it), at one specific number, recorded with
    when it was given. A different number has no consent until the member opts in at it; withdrawing it in
    the app is not an opt-out.
  - **Opt-out** is the number's: the STOP a number replied with is recorded once for the number, binds every
    league it is in, and outranks consent until the number replies START.
  - **Quiet hours** are the member's, by their own clock (21:00 to 09:00 Eastern until they choose others).
    A text that falls inside them is held until they end, not dropped.

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

## As built: texts (M3)

- The same machinery carries SMS: one handler per Treasury event records a `Notification` for each member
  it concerns (the dedupe key's recipient is the member) and a `SendNotification` for it. `DuesAssessed`,
  `PaymentConfirmed`, `PaymentRejected` and `AdjustmentPosted` text the member whose account it is,
  `PaymentAttested` texts every treasurer but the attester, and nobody is texted about what they did
  themselves. `SeasonOpened` texts nobody: each member hears of the dues assessed to them. The assess,
  attest and adjust endpoints open their session through `OutboxedSessionFactory`, for the reason above.
- Whether to text is decided when the text is about to go, not when the event is handled, so a member who
  opts out in between is not texted: no `PhoneOptOut`, then consent at the number now on record, then
  quiet hours. A notification is otherwise `Skipped` (with a reason) or `Held` (with `SendAfter`). That
  rule is `SmsDelivery.Decide` in the domain, as are the words of every text (`SmsTexts`).
- Twilio is the second `INotificationChannel`, a typed HTTP client under the resilience handler and a
  rate limit, sending from one toll-free number per deployment and asking Twilio to report delivery to
  `POST /webhooks/twilio/status?league=&notification=`. It is faked at the HTTP message handler, like
  Discord and Sleeper. Its auth token and the member's number are never logged, traced or put in an
  exception. None of its settings are in the repository.
- Twilio calls back on two signed endpoints, `/webhooks/twilio/status` (a text's delivery status, which
  only moves forward) and `/webhooks/twilio/inbound` (STOP records, START deletes, a `PhoneOptOut`); see
  [domain.md](../domain.md#twilios-callbacks) and [ADR-0005](0005-idempotency-and-concurrency.md).
- The tick that sends held texts and due-date reminders is built
  ([ADR-0007](0007-scheduled-work-under-scale-to-zero.md)). A send that fails after its retries is
  dead-lettered without failing the command, as for Discord; a send the tick makes is not, but stays
  pending for the next tick and fails the Job.
- A resilience retry of Twilio's POST can send a text twice if the first was accepted but its answer was
  lost. Twilio has no idempotency key for messages, so this is accepted for now.
