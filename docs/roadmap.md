# Roadmap

Milestones are sized to weekends. The league starts using BallBank after M2; everything after that
happens while it is live.

## M0 — Skeleton with a URL ✅ (this commit)

Solution layout, Aspire AppHost with PostgreSQL, Marten and Wolverine wired with conjoined tenancy,
`GET /v1/version`, health endpoints, the `MemberAccount` aggregate with its first five events and
their tests, four Reqnroll scenarios, PostgreSQL integration tests through Testcontainers, the Next.js
shell, CI, ADR skeletons, agent skills.

Deploy target: API on Container Apps, web on Static Web Apps, so nothing lives only on a laptop.

## M1 — Import and membership

- Typed Sleeper client (`/league/{id}`, `/league/{id}/users`, `/league/{id}/rosters`,
  `/user/{id}/leagues/nfl/{season}`, `/state/nfl`) with timeouts, retries and cached snapshots.
- `ImportLeague` → `LeagueImported`, `MemberAdded` per roster. `ImportLeagueAgain` adds a
  `MemberAdded` for each roster not yet a member; importing again with nothing new is a no-op.
- Auth0 sign-in on the web; JWT validation on the API.
- Invite links and identity claims (`MemberClaimed`); the Treasurer role, which any treasurer can give another member.
- "My leagues" and member list pages.
- Contact details (email, US phone, Discord username) in a tenant-scoped `MemberContact` document,
  not events (ADR-0012): a treasurer edits anyone's, a member their own, nobody else sees them.
- Reqnroll: *Importing a league*, *Keeping contact details*.

## M2 — The ledger (league goes live)

- `OpenSeason`, bulk `AssessDues`, `AttestPayment`, `ConfirmPayment`, `RejectPayment`, `PostAdjustment`.
- `MemberStatement` inline projection; `GET …/history`.
- `Idempotency-Key` middleware; expected-`Version` concurrency with `409`.
- Member statement page with the "I paid" form; treasurer confirmation queue.
- Reqnroll: *Rejecting a payment*, *Adjustments need a reason*, plus HTTP-level versions of the
  existing scenarios.

## M3 — Dashboard, reminders, limits, telemetry

- `LeaguePot` async projection and the projection daemon.
- Notifications: Discord webhook channel, Twilio SMS channel with opt-in, STOP handling, quiet
  hours and dedupe; delivery status callback.
- Container Apps cron Job for due-date reminders and a weekly delinquency digest.
- Per-tenant rate limiting; k6 proof.
- OpenTelemetry to Application Insights with `tenant.id` everywhere; first `numbers.md`.

## M4 — Payouts, season close, demo rail

- `PayoutStructureSet`, `StandingsDeclared` (from Sleeper records or entered), `CloseSeason` →
  process manager → `PayoutRecorded`; account close with write-off path.
- Stripe test-mode rail behind `IPaymentRail`, webhook idempotent on Stripe's event id, clearly
  labelled demo-only.
- One feature-flagged progressive rollout (OpenFeature) with a guardrail metric.
- Architecture diagram, screenshots, ADRs finished.

## Season — operate it

Weekly digests, confirmations as payments arrive, one postmortem for whatever breaks, monthly cost
and availability recorded in `numbers.md`. Season close in January: payouts recorded, books closed.

## Not planned (yet)

Player data, scoring, trades, side-bet marketplaces, real payment processing. The ledger has to run
a whole season first.
