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
2. **Run the rebuild from the same image, with the same settings** (`ConnectionStrings__ballbank`, and
   `Auth0__Domain` / `Auth0__Audience`, which the host insists on even here):

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

*(M3)* Inspect in the Wolverine dead-letter table, fix the cause, replay by id.

## Rotate a secret

*(M1)* Auth0 client secret, Neon connection string, Twilio auth token: update in Container Apps
secrets, restart the revision, confirm `/health`.

## Wake-up check before Sunday night

*(M3)* The cron Job pings the API at 18:00 ET on Sundays so the first member does not eat the cold start.

## Incident notes

Postmortems live in `docs/incidents/` with the format: timeline, impact, root cause, durable change.
