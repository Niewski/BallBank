# Runbook

Operational procedures. Each one is written the first time it is needed for real.

## Rebuild a projection

*(M3)* Stop the daemon, `dotnet BallBank.Api.dll projections --rebuild LeaguePot`, restart.

## Replay a dead-lettered message

*(M3)* Inspect in the Wolverine dead-letter table, fix the cause, replay by id.

## Rotate a secret

*(M1)* Auth0 client secret, Neon connection string, Twilio auth token: update in Container Apps
secrets, restart the revision, confirm `/health`.

## Wake-up check before Sunday night

*(M3)* The cron Job pings the API at 18:00 ET on Sundays so the first member does not eat the cold start.

## Incident notes

Postmortems live in `docs/incidents/` with the format: timeline, impact, root cause, durable change.
