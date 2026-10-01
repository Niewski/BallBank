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

Only `LeaguePot` has been rebuilt this way so far.

## Replay a dead-lettered message

A notification that Discord would not take is retried after 5 seconds, 30 seconds and 5 minutes
(`Notifications:RetryDelays`) and then moved to the dead-letter queue. Nothing else is held up: the
confirmation or season that caused it is committed and answered. The `Notification` document stays
`Pending`, which is how you tell a notification that failed for good from one that is merely waiting.
Written from making a fake Discord refuse, not yet from a real outage.

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
   again is Wolverine's way of asking for a replay: it moves them back to the inbox on a later
   durability pass. That step has not been run here yet; the tests stop at the dead letter. What is
   proven is that a send is safe to repeat: it finds the notification `Pending`, posts it and marks it
   `Sent`, while one already `Sent` is skipped, so replaying twice tells the channel once.
4. **Check.** The notification is `Sent`, and the row is gone from `wolverine_dead_letters`.

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

## Rotate a secret

*(M1)* Auth0 client secret, Neon connection string, Twilio auth token: update in Container Apps
secrets, restart the revision, confirm `/health`.

The API refuses to start without `Notifications__WebBaseUrl`, where the web app is served, which the
links in a text point to. The Twilio settings are `Twilio:AccountSid`, `Twilio:AuthToken` (the one to rotate), `Twilio:FromNumber`
(the deployment's one toll-free number) and `Twilio:StatusCallbackBaseUrl` (where Twilio reports delivery,
the API's public address). Locally they are `dotnet user-secrets` on `BallBank.Api`; deployed they are
platform settings (`Twilio__AuthToken` …), never the repo. A machine without them still decides, records
and queues texts, but every send fails and is dead-lettered after its retries (see "Replay a
dead-lettered message"), so a rotated token that does not take shows as dead letters naming
`SendNotification`, not as a failed command. After rotating, confirm with a text to a member who has
opted in.

## Wake-up check before Sunday night

*(M3)* The cron Job pings the API at 18:00 ET on Sundays so the first member does not eat the cold start.

## Incident notes

Postmortems live in `docs/incidents/` with the format: timeline, impact, root cause, durable change.
