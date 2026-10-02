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
   OpenTelemetry ──▶ Application Insights, and any OTLP collector; locally the Aspire dashboard
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
replica, Redis when there are more ([ADR-0013](adr/0013-every-league-has-its-own-request-budget.md)).
What a league's requests, messages, ticks and projections record is tagged with `tenant.id`
([Observability](#observability)).
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
  on the same channel (dedupe key: channel + recipient + kind + cause, the id of the `Notification`
  document).
- **Delivery through the outbox.** Marten forwards a Treasury event to its Wolverine handler in the
  transaction that commits it; the handler records a `Notification` and a `SendNotification` message on
  a durable local queue, in that same transaction. Retries with backoff; poison messages to the
  dead-letter queue; delivery status recorded from Twilio's status callback. Built so far (M3):
  Discord announcements of a season opening and of confirmed payments, configured per league by its
  treasurers, and texts to opted-in members about their own account and to treasurers about
  attestations, checked for consent and quiet hours as they are sent, with Twilio's signed callbacks
  recording each text's delivery status and a member's STOP and START, and, from the tick, due-date
  reminders and the release of held texts
  ([domain.md](domain.md#notifications), [ADR-0008](adr/0008-buy-auth-abstract-flags-free-notifications.md)).
- **Cost.** Discord is free. SMS is the one paid line item: a toll-free number is about $2.15/month
  plus roughly a cent per message including carrier fees; toll-free verification is required for
  US traffic and takes a few business days. A twelve-member league sending a handful of reminders
  a month is a few dollars. Each attempt to send is counted by channel, kind and outcome, so the texts
  actually sent are a sum, not a guess ([Observability](#observability)).

## Idempotency, concurrency, outbox

See [domain.md](domain.md#idempotency-and-concurrency). In short: idempotency keys on every mutating
request, expected versions on every command, events and outgoing messages in one transaction.

## Scheduled work under scale-to-zero

The API scales to zero between requests, so Wolverine's in-process scheduled messages only fire while
something is awake. Reminders and digests therefore run from a Container Apps cron **Job** built
from the same image (`dotnet BallBank.Api.dll tick`), hourly, which does what is due as of now from the
books and exits; it does not call the API. Keeping one replica warm would cost more than the rest of the
system combined ([ADR-0007](adr/0007-scheduled-work-under-scale-to-zero.md)). The weekly digest is not
built yet.

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
| Telemetry | Application Insights, through the Azure Monitor exporter | Within the monthly free ingestion allowance; alternative: Grafana Cloud free tier, through the OTLP exporter that is already there. |
| Notifications | Discord webhook (free), Twilio SMS (paid, small) | See above. |

Expected bill: a few dollars a month, almost all SMS. The actual bill is published monthly in
[numbers.md](numbers.md).

## Observability

OpenTelemetry, set up once in `ServiceDefaults` (`AddServiceDefaults()`) for the API and the Job, which are
one image.

- **Traces:** every request but the health probes, every message Wolverine handles, and the application's
  own spans. Outgoing HTTP calls are traced too, except Discord's, whose webhook URL is a secret carried in
  its path ([ADR-0008](adr/0008-buy-auth-abstract-flags-free-notifications.md)).
- **Metrics:** ASP.NET Core, `HttpClient`, the runtime, and every meter named `BallBank.*`:
  `ballbank.projection.lag`, `ballbank.ratelimit.rejections` and `ballbank.notifications`.
- **Logs:** everything `ILogger` writes, with its scopes and its trace and span ids.

**One league at a time.** Any question about one league is answered by filtering on `tenant.id`:

| What | Carries `tenant.id` | Taken from |
|---|---|---|
| A request | its span, and every log written while it is handled | the route (`UseTenantTelemetry`) |
| A message handled | the handler's span, and every log written while the handler runs (Marten's statements among them, when its log level lets them through) | the message's tenant: Wolverine tags the span, `MessageTenantLogScope` opens the log scope |
| The tick | each league's lines, the summary `Tick for league …` among them | a scope per league, in `Tick` |
| The projection daemon | the line the projection writes for each league in a batch it applies, and `ballbank.projection.lag` | the events' tenant, as `LeaguePotProjection` reads it |
| `ballbank.notifications` | every count | the league the notification is for |

What the framework logs before the league is known (a request starting) or around a handler (a message
arriving) is written outside those scopes and has its trace id only; the trace finds it. Deployed, Marten
logs at `Warning` and the notification handler logs nothing itself, so a question about one league's messages
is answered by the handler's span and the notifications counter, not by its logs.

**Notifications.** `ballbank.notifications` counts each attempt to deliver one, tagged `tenant.id`, `channel`
(`Sms`, `Discord`), `kind` (`Reminder`, `PaymentConfirmed`, …) and `outcome`: `Sent`; `Held` (the member's quiet
hours); `Skipped` (no consent, opted out, or a reminder to a member who has since paid up); `Dropped` (the
league disconnected the channel); or `Failed` (the channel refused the send, which is tried again). "Did anyone
get texted this week?" is the `Sent` count for channel `Sms`, and what Twilio will charge is that count at the
per-message rate under Notifications.

**Where it goes.** Two exporters, each switched on by its own setting and independent of the other:

- OTLP, when `OTEL_EXPORTER_OTLP_ENDPOINT` is set. Aspire sets it locally, and the dashboard receives it.
- Application Insights, when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set, through the Azure Monitor
  exporter: the exporter package, not the Azure Monitor distro, which would instrument the process a second
  time and skip the Discord filter. The setting belongs to the platform and never to the repo; without it
  nothing is sent to Azure and nothing else changes, which is how it runs locally
  ([runbook](runbook.md#send-telemetry-to-application-insights)).

Two choices that are not obvious from the code:

- **Every trace is kept.** The Azure Monitor exporter's default sampler lets five traces a second through,
  for the whole provider (the OTLP export too), and drops the logs of the traces it drops. A league's traffic
  is small enough to keep all of it.
- **The Job's telemetry is built and flushed by hand.** Its host is built and never started, and starting a host
  is what builds the telemetry providers. So `tick` builds them before the work and flushes them after it
  (`StartTelemetry`), waiting at most ten seconds for each signal. For the same reason there is one Azure
  exporter per signal rather than `UseAzureMonitorExporter`, which attaches its trace and log exporters only
  once a host starts.

Published numbers: cold start, warm p50/p95 for `ConfirmPayment`, `LeaguePot` projection lag,
availability over the season, rate-limit proof, events per season, monthly cost.
