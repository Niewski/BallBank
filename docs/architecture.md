# Architecture

```
 Browser ── Next.js (static export, Azure Static Web Apps) ──HTTPS/CORS──▶ API (.NET 10, Azure Container Apps, 0→1 replicas)
                                                                             │  Marten + Wolverine
                                                                             ▼
                                                                      Neon PostgreSQL (scale-to-zero)
   Sleeper API ◀────── import (typed client, retries, cached snapshots) ─────┘
   Discord webhook ◀── Notifications (Wolverine outbox)
   Twilio SMS      ◀── Notifications (opt-in only; delivery status webhook ──▶ API)
   Stripe test mode ── webhook ──▶ /webhooks/stripe   (demo rail only, never live)
   OpenTelemetry ──▶ Application Insights (or Grafana Cloud); locally the Aspire dashboard
   Container Apps cron Job (same image) ──▶ due-date reminders, weekly delinquency digest
```

## Components

**API** (`src/BallBank.Api`). ASP.NET Core with Wolverine.HTTP endpoints organised as vertical slices
under `Features/`. Marten is the event store and document store; Wolverine provides command handling
with the aggregate-handler workflow (the handler receives the rehydrated aggregate, returns events,
Wolverine appends them and commits the outbox in the same transaction), scheduled messages and the
dead-letter queue.

**Domain** (`src/BallBank.Domain`). Pure C#, no packages. See [domain.md](domain.md).

**Web** (`web/`). Next.js, exported as static files; calls the API cross-origin with a bearer token.

**AppHost** (`src/BallBank.AppHost`). Aspire orchestration for local development: PostgreSQL
container with a named volume, the API, the Next.js dev server, and the dashboard.

## Tenancy

Tenant = league. Marten conjoined tenancy puts `tenant_id` on every event and document row; sessions
are opened per tenant. The tenant is derived server-side from the route (`/leagues/{leagueId}/…`)
after authorisation confirms the caller is a member of that league — never from the request body.
Row-level security is available as defence in depth. Per-tenant rate limiting uses ASP.NET Core's
partitioned token bucket keyed by league (`429` + `Retry-After`); in-process while there is one
replica, Redis when there are more. Every log line, trace and metric is tagged with `tenant.id`.
Database-per-tenant is the "higher tier" option and is confined to store configuration
([ADR-0004](adr/0004-conjoined-tenancy.md)).

## Identity and roles

Sign-in is delegated to a managed identity provider (Auth0) issuing JWTs; the API validates bearer
tokens. Sleeper has no OAuth, so a member *claims* their imported team through an invite link: sign
in, pick your team, the treasurer approves (or the invite pre-approves). Roles are BallBank's:
whoever imports the league becomes Treasurer; Sleeper's `is_owner` users are offered co-treasurer.

## Notifications

Two channels, one abstraction: `INotificationChannel` with `DiscordWebhookChannel` and
`TwilioSmsChannel`, driven by Wolverine handlers reacting to Treasury events and by scheduled
messages for reminders. Design points:

- **Consent.** SMS is opt-in per member, recorded with a timestamp; STOP/START are honoured (Twilio
  handles the keywords, the API records the resulting state). Discord goes to a league channel the
  treasurer configures.
- **Preferences.** Each member chooses channels and quiet hours. The same event is never sent twice
  on the same channel (dedupe key: member + event id + channel).
- **Delivery through the outbox.** A notification is a message committed with the event that caused
  it; retries with backoff; poison messages to the dead-letter queue; delivery status recorded from
  Twilio's status callback.
- **Cost.** Discord is free. SMS is the one paid line item: a toll-free number is about $2.15/month
  plus roughly a cent per message including carrier fees; toll-free verification is required for
  US traffic and takes a few business days. A twelve-member league sending a handful of reminders
  a month is a few dollars.

## Idempotency, concurrency, outbox

See [domain.md](domain.md#idempotency-and-concurrency). In short: idempotency keys on every mutating
request, expected versions on every command, events and outgoing messages in one transaction.

## Scheduled work under scale-to-zero

The API scales to zero between requests, so Wolverine's in-process scheduled messages only fire while
something is awake. Reminders and digests therefore run from a Container Apps cron **Job** built
from the same image (`dotnet BallBank.Api.dll tick`), which wakes the app on a schedule. Keeping one
replica warm would cost more than the rest of the system combined
([ADR-0007](adr/0007-scheduled-work-under-scale-to-zero.md)).

## Hosting and cost

| Component | Service | Notes |
|---|---|---|
| API | Azure Container Apps, consumption plan, `minReplicas: 0`, `maxReplicas: 1` | The monthly free grant (180,000 vCPU-s, 360,000 GiB-s, 2M requests) covers a league many times over. Cold start a few seconds; measured in [numbers.md](numbers.md). |
| Scheduled work | Container Apps Job (cron) | Same consumption meter. |
| Database | Neon PostgreSQL, Free plan | 0.5 GB and 100 compute-hours per project; suspends after 5 minutes idle. A season of one league is a few hundred events. |
| Web | Azure Static Web Apps, Free plan | Static export, custom domain, managed certificate. |
| Images | GitHub Container Registry (public) | Avoids a paid registry. |
| CI/CD | GitHub Actions | OIDC federation to Azure; no stored cloud credentials. |
| Identity | Auth0, free tier | JWT bearer to the API. |
| Telemetry | Application Insights | Within the monthly free ingestion allowance; alternative: Grafana Cloud free tier. |
| Notifications | Discord webhook (free), Twilio SMS (paid, small) | See above. |

Expected bill: a few dollars a month, almost all SMS. The actual bill is published monthly in
[numbers.md](numbers.md).

## Observability

OpenTelemetry from `ServiceDefaults`: traces for every request and handler, metrics for latency and
queue depth, structured logs with correlation ids. Locally everything lands in the Aspire dashboard.
Published numbers: cold start, warm p50/p95 for `ConfirmPayment`, `LeaguePot` projection lag,
availability over the season, rate-limit proof, events per season, monthly cost.
