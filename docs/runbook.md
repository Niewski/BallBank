# Runbook

Operational procedures. Each one is written the first time it is needed for real.

## Rebuild a projection

When a projection's document is wrong, or its code changed how it folds events. The events are the
truth: the async projection `LeaguePot` can be thrown away and replayed from them, and comes back the
same (`A_rebuild_reproduces_the_same_document` proves it on every build). Written from doing it against
a local PostgreSQL holding a mixed season, after deleting the `LeaguePot` document to see it return.

1. **Stop the daemon: stop the API.** Locally, stop `aspire run`; deployed, scale the app to zero
   replicas. The rebuild does not check for a running daemon (it ran beside a live one without a
   word), and a live daemon goes on applying new events to the documents being replayed.
2. **Run the rebuild from the same image, with the same settings** (`ConnectionStrings__ballbank`,
   `Auth0__Domain` / `Auth0__Audience` and `Notifications__WebBaseUrl`, which the host insists on even here):

   ```
   dotnet BallBank.Api.dll projections rebuild -p LeaguePot
   ```

   It prints `Finished rebuilding LeaguePot in … ms` and `Projection Rebuild complete!` and exits `0`.
   The flag is `-p` / `--projection`, and `rebuild` is a verb: there is no `--rebuild`.
   `dotnet BallBank.Api.dll projections list` shows the projections the image knows and their
   lifecycle. The line `Cannot override services when the IHost is already constructed` printed
   first every time is harmless.
3. **Start the API again.** The daemon carries on from the progress recorded for it:

   ```sql
   select name, last_seq_id from ballbank.mt_event_progression;
   ```

   `LeaguePot:All` reaches `HighWaterMark` once it has caught up.
4. **Check.** Open the season's dashboard: it says when it was last updated. `ballbank.projection.lag`
   measures how old an event is when it is applied, so a replay records every event as old; large
   values from the process that ran the rebuild are the replay, not a stalled daemon.

`dotnet BallBank.Api.dll projections list` shows `LeaguePot` as the one `Async` projection; the others are `Live`
(folded when read) or `Inline` (folded as the events commit), so it is the only one with a daemon to stop. Only
`LeaguePot` has been rebuilt this way so far. A rebuild against a database with no events says `The event
storage … is empty, aborting` and exits `0`: if that is not what the season looks like, the connection string
points at the wrong database.

## Replay a dead-lettered message

A notification that Discord would not take is retried after 5 seconds, 30 seconds and 5 minutes
(`Notifications:RetryDelays`) and then moved to the dead-letter queue. Nothing else is held up: the
confirmation or season that caused it is committed and answered. The `Notification` document stays
`Pending`, which is how you tell a notification that failed for good from one that is merely waiting.
Written from doing it end to end against a real PostgreSQL, with a fake Discord that refuses and is then mended
(`A_dead_lettered_send_is_delivered_once_it_is_marked_replayable_and_Discord_takes_it`), not yet from a real outage.

1. **Find them.** Dead letters live in the `ballbank` schema, and the body is `bytea`:

   ```sql
   select id, message_type, exception_type, sent_at, replayable
   from ballbank.wolverine_dead_letters
   where message_type like '%SendNotification%'
   order by sent_at;

   select id, data ->> 'Text' as text, data ->> 'CreatedAt' as created_at
   from ballbank.mt_doc_notification
   where data ->> 'Status' = 'Pending';
   ```

   The webhook URL is never in a dead letter: the message names the league and the notification, and
   the webhook is read from `LeagueNotificationSettings` when it is sent. `encode(body, 'escape')` shows
   what a dead letter carried.
2. **Fix the cause.** `Discord refused the webhook` in the exception means the channel or webhook was
   deleted or mistyped: a treasurer connects Discord again (`PUT …/notifications/discord`). A league that
   disconnected in the meantime is not posted to; its pending notifications are marked `Dropped` when
   the send runs.
3. **Replay.** Rows are dead-lettered with `replayable = false`. Setting it to `true` on the ones to send
   again is Wolverine's way of asking for a replay:

   ```sql
   update ballbank.wolverine_dead_letters set replayable = true
   where message_type like '%SendNotification%' and encode(body, 'escape') like '%<notification id>%';
   ```

   The API's own durability pass moves them back to the inbox and sends them, so the API has to be running:
   ask it for `GET /v1/version` and leave it a minute. Locally the send went out about six seconds after the
   update. Mend the cause first (step 2): a send that is refused again is retried and dead-lettered again.
   Replaying is safe to repeat: the send finds the notification `Pending`, posts it and marks it `Sent`, while
   one already `Sent` is skipped, so replaying twice tells the channel once.
4. **Check.** The notification is `Sent` with a `SentAt`, the row is gone from `wolverine_dead_letters`, and
   the channel has the post:

   ```sql
   select data ->> 'Status' as status, data ->> 'SentAt' as sent_at
   from ballbank.mt_doc_notification where id = '<notification id>';

   select count(*) from ballbank.wolverine_dead_letters where encode(body, 'escape') like '%<notification id>%';
   ```

## A league is being refused with 429

([ADR-0013](adr/0013-every-league-has-its-own-request-budget.md))

1. **Find who.** `ballbank.ratelimit.rejections` is tagged `partition` (`league`, `caller`, `webhook`)
   and, for a league, `tenant.id`. A refused request's trace carries `tenant.id` too.
2. **Decide whether it is a runaway or real use.** A client stuck retrying is the former: ask whoever
   is running it to stop. A league that has outgrown its budget is the latter.
3. **Raise the budget.** Set `RateLimits__League__Burst` and `RateLimits__League__Refill` (or
   `__Caller__`, `__Webhook__`; `__Every` is a time span, `00:00:01` by default) in the container's
   settings and restart the revision. The defaults are in `RateLimitOptions`. A setting applies to every
   league; there is no override for one.
4. **Behind the ingress**, `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` has to be set, or every webhook
   shares one address and one budget.

## The tick, and when it fails

([ADR-0007](adr/0007-scheduled-work-under-scale-to-zero.md))

The Container Apps Job runs `dotnet BallBank.Api.dll tick` hourly, from the API's image, with the
API's settings (the Neon connection string, the Twilio settings, `Notifications__WebBaseUrl`); "The Jobs"
below defines it. For each league with an open season it sends the due-date reminders, releases held
texts, posts the weekly digest to Discord if the league asked for it, and purges what is older than the
retention age, then exits.

- **What it logged.** One `Information` line per league, `Tick for league {LeagueId}, season {Season}:
  sent {Sent}, held {Held}, skipped {Skipped}, failed {Failed}, digests {Digests}, purged {n} idempotency
  records and {n} notifications`, in the Job's logs with the league's `tenant.id`. `digests` is 1 on the
  tick whose digest Discord took, and the two `purged` counts are what the purge deleted. A league the
  tick could not finish is an `Error` line with the exception and makes the Job exit non-zero (a failed
  execution); the other leagues were done regardless. `Another tick is running` means a previous
  execution overran; this one did nothing, which is safe.
- **`failed` above zero** is a send the channel refused (Twilio down, a rotated token that did not take, a
  Discord webhook that was deleted). It also makes the Job exit non-zero, so the execution shows as
  failed. Those messages stay `Pending`, and the next hourly tick sends them again (a reminder after
  checking the member still owes, a digest while its week is still the one due), so there is nothing to
  replay: fix the cause and let the next execution run.
- **The connection string** the Job uses must reach Postgres directly. The tick holds a session
  advisory lock for its run, which a transaction-pooled endpoint (a pooler in front of the database)
  does not keep.
- **A missed hour loses nothing.** What is due comes from the books and the clock, so the next execution
  does it. To catch up by hand, start the Job (or run the command in a console on the revision) once.
- **Running it twice is safe.** A reminder is sent once per account, due date and stage, and a second
  execution, or one overlapping the first, finds that and does nothing.
- **A reminder did not arrive.** On the dashboard, the delinquent's last reminder says whether it was
  `Sent` (and, once Twilio's status callback has come back, `Delivered`, or `Undelivered` or `Failed`
  with Twilio's error), `Held` (their quiet hours; the tick sends it when they end), or `Skipped` (no
  consent at their number, or the number opted out), and why. None at all means no tick has found them
  due yet.
- **The digest did not arrive.** The tick posts it once per league per ISO week, in the first tick at or
  after Monday 09:00 Eastern, and only to a league with an open season that has connected Discord and
  turned on "Post the weekly digest". A tick before that slot posts nothing; that is not a failure. A late
  tick still posts it, any time up to Sunday night, and a week no tick found is not made up. The week's
  `Notification` is keyed `Discord/{leagueId}/Digest/{year}-W{week}`:

  ```sql
  select tenant_id as league, id, data ->> 'Status' as status, data ->> 'Reason' as reason,
         data ->> 'CreatedAt' as created_at
  from ballbank.mt_doc_notification
  where data ->> 'Kind' = 'Digest'
  order by data ->> 'CreatedAt' desc;
  ```

  No row for the week: the league has not asked for it, disconnected Discord, has no open season, or
  the slot has not come. `Sent`: Discord took it, so look at the channel and its webhook. `Pending`:
  Discord refused it, and each hourly tick tries again until the week is over. `Skipped`, "The digest is no
  longer due": the week ended, or the league turned the digest off or disconnected Discord (which forgets
  the settings), before Discord took it. `Dropped` reads the same and is rare: the league disconnected
  between the tick's check and its send.
- **What the purge deletes.** Each tick, league by league, deletes the idempotency records (the answers
  kept for retried requests, [ADR-0005](adr/0005-idempotency-and-concurrency.md)) and the notification
  records (what was sent, held or skipped, and the keys that stop a reminder or a digest being sent twice)
  written more than `Retention__Days` days ago: 90 unless set. Events, statements and settings are
  not touched. The summary line says how many it deleted. It is what keeps the answers nobody will ask for
  again from filling the free Neon plan's 0.5 GB. It visits the leagues the rest of the tick does, those
  with an open season, so a league with none keeps its few records.
- **`Retention__Days` has a floor of 14.** Deleting a notification frees its key, so a record has to outlive
  the week a digest is due in and the week a reminder's stage lasts. The API refuses to start below the
  floor (`Retention:Days must be at least 14.`). The Job does not start the host, so it finds out when it
  first purges, after the league's reminders and digest have gone: that league is logged as one the tick
  "did not finish" with the same message, nothing is deleted, and the Job exits non-zero. Set it back to
  14 or more, or unset it.
- **Its telemetry** is sent before it exits. With `APPLICATIONINSIGHTS_CONNECTION_STRING` set on the Job
  ([below](#send-telemetry-to-application-insights)), the per-league lines and `ballbank.notifications` (what the
  tick sent, held, skipped and could not send) reach Application Insights, and the Job waits at most ten seconds
  for each of its traces, metrics and logs at the end. An execution that is slow to exit with nothing else
  wrong is that wait: Application Insights was slow to answer.

## Send telemetry to Application Insights

([architecture](architecture.md#observability))

The API and the Job send their traces, metrics and logs to Application Insights when
`APPLICATIONINSIGHTS_CONNECTION_STRING` is set and, independently, to an OTLP collector when
`OTEL_EXPORTER_OTLP_ENDPOINT` is. Written against a fake of Application Insights on loopback, which proves what
leaves the process and what it carries; the portal itself has not been looked at yet.

1. **Set the connection string** on the API's container app and on the Job, copied from the Application
   Insights resource. It is a platform setting (a Container Apps secret the app's environment refers to),
   never a file in the repo, and never `appsettings`. Locally it stays unset and the Aspire dashboard receives
   the telemetry. A blank value counts as unset.
2. **Name each one.** The API and the Job run the same assembly, so without `OTEL_SERVICE_NAME` both are
   `unknown_service:dotnet`, one role in Application Insights. Set it on the API (say `ballbank-api`) and on the Job
   (`ballbank-tick`).
3. **Restart the revision**; the Job reads it on its next execution.
4. **Check.** A minute or two after a request (metrics go out every minute), in the resource's Logs:

   ```kusto
   union requests, dependencies, traces
   | where timestamp > ago(15m)
   | summarize count() by cloud_RoleName, tostring(customDimensions["tenant.id"])
   ```

   There is a row per league under each role, and one with an empty `tenant.id` for what was written outside
   any league's scope. No rows at all means the setting did not reach the process: look in the revision's
   environment, and for the Job's rows, check it was set on the Job and not only on the API.

Every trace is exported, not a sample. A league's traffic is small, but the resource's ingestion is still to be
watched against the monthly free allowance ([architecture](architecture.md#hosting-and-cost)).

## Ask the telemetry about one league

Everything a league's requests, messages, ticks and projections record carries `tenant.id`, the league's id
([architecture](architecture.md#observability)). In the Aspire dashboard, filter traces, logs or metrics on it. In
Application Insights it is a custom dimension on every table:

```kusto
// What happened to one league: its requests, handlers, ticks and projection batches, newest first
union requests, dependencies, traces, exceptions
| where customDimensions["tenant.id"] == "<league id>"
| order by timestamp desc

// Did anyone get texted this week? What will Twilio charge: the Sent count, at the per-message rate
customMetrics
| where name == "ballbank.notifications" and timestamp > ago(7d)
| where customDimensions["channel"] == "Sms"
| summarize attempts = sum(value) by tostring(customDimensions["tenant.id"]), tostring(customDimensions["kind"]), tostring(customDimensions["outcome"])
```

`outcome` is `Sent`, `Held` (quiet hours), `Skipped` (no consent, opted out, a reminder to someone who has since
paid, or a digest no longer due), `Dropped` (the league disconnected the channel) or `Failed` (the channel refused the send; the retry or the
next tick counts again). A text held at night and sent in the morning is one `Held` and one `Sent`. The same
counter answers it for Discord with `channel` `Discord`. What the framework logs before the league is known (a
request starting) or around a handler (a message arriving) has the trace id but not `tenant.id`: find the span, then
follow its `operation_Id`.

## Rotate a secret

Three secrets are BallBank's to rotate: the Neon connection string (`ConnectionStrings__ballbank`), the
Twilio auth token (`Twilio__AuthToken`) and, if Application Insights is on, its connection string
(`APPLICATIONINSIGHTS_CONNECTION_STRING`). There is no Auth0 client secret to rotate: the API only validates
tokens against `Auth0__Domain` and `Auth0__Audience`, which are not secrets, and the web app is a static export.
A league's Discord webhook URL is a secret too, but it is the league's: if it leaks, a treasurer makes a new
webhook in Discord and connects it (`PUT …/notifications/discord`).

Written from the code and from running the API locally (a wrong database password stops it at start-up);
no deployed secret has been rotated yet, so correct what differs the first time.

1. **Make the new value at the provider** (Neon: reset the role's password; Twilio: promote a new auth token).
   The old value stops working there, so expect failures until the next two steps are done.
2. **Update it in Container Apps secrets**, on the API *and* on the tick Job: each holds its own copy.
3. **Restart the API's revision.** The API connects to Postgres as it starts, and with a wrong password it does
   not start (`28P01: password authentication failed`, exit code 1), so a revision that stays up and answers
   `GET /v1/version` has a working connection string. Do not check `/health`: it exists only in Development.
4. **Check the Job.** Start one tick, or wait for the next, and read its execution as "The tick, and when it
   fails" does: `Succeeded` means it reached Postgres (and Twilio, if anyone was due a text).

The API refuses to start without `Notifications__WebBaseUrl`, where the web app is served, which the
links in a text point to. The Twilio settings are `Twilio:AccountSid`, `Twilio:AuthToken` (the one to rotate), `Twilio:FromNumber`
(the deployment's one toll-free number) and `Twilio:StatusCallbackBaseUrl` (where Twilio reports delivery,
the API's public address). Locally they are `dotnet user-secrets` on `BallBank.Api`; deployed they are
platform settings (`Twilio__AuthToken` …), never the repo. A machine without them still decides, records
and queues texts, but every send fails and is dead-lettered after its retries (see "Replay a
dead-lettered message"), so a rotated token that does not take shows as dead letters naming
`SendNotification`, not as a failed command. After rotating, confirm with a text to a member who has
opted in.

Twilio's callbacks (`/webhooks/twilio/status` and `/webhooks/twilio/inbound`) are signed with the same
auth token. The number's incoming-message webhook, in Twilio's console, must be
`{Twilio:StatusCallbackBaseUrl}/webhooks/twilio/inbound`, and `Twilio:StatusCallbackBaseUrl` must be the
exact address Twilio calls (scheme, host and any path prefix), because the signature covers the URL as
Twilio saw it. If every callback is answered `403`, look for the warning "its Twilio signature did not
match" in the API's logs: the cause is a base URL that differs from the public one, or a token that
was rotated on one side only. Texts still go, but their status stays `Sent`, and a STOP is not recorded.

## The Jobs

([ADR-0007](adr/0007-scheduled-work-under-scale-to-zero.md), [ADR-0009](adr/0009-container-apps-and-neon.md))

Two Container Apps Jobs run in the API's environment, on a cron schedule. **Neither exists yet**: nothing
is deployed and the repo holds no infrastructure code, so this is the definition to create them from,
not a record of having done it. Correct what differs the first time they are created.

| | The tick | The Sunday wake-up |
|---|---|---|
| Does | the tick: reminders, held texts, the weekly digest, the purge | asks the API for its version, which starts it if it was stopped |
| Trigger | Schedule, cron `0 * * * *` | Schedule, cron `0 22 * * 0` |
| Image | the API's image, the tag the API runs | `curlimages/curl` |
| Runs | `dotnet BallBank.Api.dll tick` | `curl --fail --silent --show-error --max-time 120 https://{the API's public address}/v1/version` |
| Parallelism, completions | 1, 1 | 1, 1 |
| Replica timeout, retries | 600 seconds, none: the next hour is the retry | 180 seconds, none |
| Settings | the API's, below | none |

- **Cron is in UTC**, whatever the league's clock says. Hourly is hourly anywhere. Sunday 18:00 Eastern is
  22:00 UTC while daylight saving time is on and 23:00 UTC while it is not, so `0 22 * * 0` is on time in
  summer and an hour early in winter, the safe side for a wake-up. Keeping it on time all year means
  editing the cron when the clocks change (`0 23 * * 0` in winter).
- **The tick's settings are the API's**: `ConnectionStrings__ballbank` (direct to Postgres, not through a
  pooler, see above), `Auth0__Domain`, `Auth0__Audience`, `Notifications__WebBaseUrl` and the
  `Twilio__…` settings (see "Rotate a secret"), as platform settings and secrets, never the repo's.
  `Retention__Days` only to change the 90. `APPLICATIONINSIGHTS_CONNECTION_STRING` (the API's) and its own
  `OTEL_SERVICE_NAME` send its telemetry ([Send telemetry to Application Insights](#send-telemetry-to-application-insights)).
- **The wake-up uses `/v1/version`** because it is the one address the API answers anonymously in every
  environment. (`/health` and `/alive` exist only in Development.) The request reads no database itself,
  but the API connects to Postgres as it starts (it applies its schema and starts its background workers),
  so the ping most likely wakes Neon too. That is expected, not measured: see step 3 below.
- **A Job's health is its execution history**: `Succeeded` or `Failed`, in the portal or from
  `az containerapp job execution list`. `curl --fail` exits non-zero when the API does not answer, or
  answers with an error, so a failed wake-up shows as a failed execution.

## Wake-up check before Sunday night

Sunday evening is the league's busiest hour, and the API stops after it has been asked for nothing for
five minutes (the consumption plan's scale-down cooldown, 300 seconds unless it was changed). The wake-up
Job asks at 18:00 Eastern, so the first member to open the app does not wait for the API to start. Written
from the definition above, not from deployed Jobs, which do not exist yet; what could be checked without them was
checked against the API running locally: `GET /v1/version` answers `200` and `{"name":"BallBank","version":…}` with no
token, and `/health` and `/alive` answer `404` outside Development, so the wake-up's address is the right one.

1. **Last Sunday's wake-up ran.** The wake-up's execution history has one at the cron's time that
   `Succeeded`. A `Failed` one has curl's error in its log: the API did not answer within 120 seconds, or
   answered with an error.
2. **The tick has been running.** The tick's history has an execution within the last hour, and it
   `Succeeded`. A `Failed` one is read as in "The tick, and when it fails".
3. **The API answers after sleeping.** More than five minutes after the last request, `GET /v1/version`
   answers `{"name":"BallBank","version":…}`; how long it takes is the cold start the wake-up spares a
   member, Neon's wake included if the ping reaches it (record it in [numbers](numbers.md)).
4. **The clocks changed since the cron was set?** It is in UTC, so the ping now lands an hour earlier or
   later by the league's clock: see "Cron is in UTC" above.

One ping covers the five minutes after it: a member who opens the app at 19:00 meets a sleeping API again.
Holding it for the hour takes a ping inside every cooldown (for example `*/4 22 * * 0`, every four
minutes of the 22:00 UTC hour), which keeps the API running for the hour: at most about 900 vCPU-seconds
a Sunday at the smallest size, some 4,000 a month, of the 180,000 the free grant covers (ADR-0007).

## Incident notes

Postmortems live in `docs/incidents/` with the format: timeline, impact, root cause, durable change.
