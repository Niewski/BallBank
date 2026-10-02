# Numbers

Measured, not estimated. A figure is here because someone measured it, with when and how. A row that
needs the deployed environment stays at "—" until a person runs its command ([Measuring by
hand](#measuring-by-hand)); nothing in it is a guess. Updated monthly while the league is live.

| Metric | Value | Measured | How |
|---|---|---|---|
| Cold start (Container Apps 0→1 + Neon wake) | — | For a person | First request after 15 min idle, p50 of 10 samples: [command](#cold-start) |
| Warm p50 / p95, confirming a payment (`POST …/attestations/{id}/confirmation`) | — | For a person | k6, 5 rps for 2 min: [command](#warm-latency) |
| `LeaguePot` projection lag, p95 | **0.68 to 1.15 s locally** (six runs; p50 0.5 to 0.7 s, max up to 1.15 s). Deployed: — | 2026-10-02, locally; deployed for a person | Time from an event being recorded to `LeaguePot` applying it (`ballbank.projection.lag`), 40 samples per run, from `SeasonMeasurementTests` ([command](#projection-lag)). Local means the API in the test process and PostgreSQL 17 in Docker on a laptop, not Container Apps and Neon, so it is a floor |
| Rate limit proof | — | For a person | k6: 429s for one tenant while another tenant is unaffected: [command](#rate-limit-proof) ([ADR-0013](adr/0013-every-league-has-its-own-request-budget.md)). The tests for it pass locally under lowered limits; this is the proof against the deployed defaults |
| Events written, one modelled season | **49** (a fixture's history, not a league's season) | 2026-10-02, locally | `SeasonMeasurementTests`: the Holland Hogs fixture league imported and its season opened, a fund assessed, attestations confirmed and one rejected, a refund and a waiver, and two members paying in five instalments each; counted with `Events.QueryAllRawEvents()` for the league |
| Events written this season | — | For a person | `select count(*) from ballbank.mt_events where tenant_id = '<league id>'`, against the deployed database |
| Availability (season) | — | For a person | External uptime monitor, 5 min interval. Point it at the web app, or at an API path that needs no database and an interval over 5 min: a probe every 5 min keeps the API awake and defeats scale-to-zero ([ADR-0007](adr/0007-scheduled-work-under-scale-to-zero.md)) |
| Deploys / rollbacks | — | For a person, once a deploy workflow exists | GitHub Actions history: `gh run list --workflow <deploy workflow> --limit 100 --json conclusion,createdAt` |
| Test suites (count, runtime) | **889 tests**: 331 domain, 117 specs (13 of them run only over HTTP), 441 integration. Locally 5 min 13 s; in CI, 6 min for the job: its *Test* step took 4 min 22 s and *Specs over HTTP* 47 s | Count and local runtime: 2026-10-02, locally. CI: run [37015318374](https://github.com/Niewski/BallBank/actions/runs/37015318374) on main at `d12e76d`, which had 886 tests, 3 fewer integration tests than now | See [Test suites](#test-suites) |
| Monthly bill | — | For a person | Azure + Neon + Twilio invoices, by service |

## How the local rows were measured

### Projection lag

```
dotnet test tests/BallBank.Integration.Tests --filter "FullyQualifiedName~SeasonMeasurementTests" --logger "console;verbosity=detailed"
```

The test (needs Docker) listens to the `ballbank.projection.lag` histogram the way the deployed API
exports it, runs a season of commands against the in-memory API over a throwaway PostgreSQL, waits for the
daemon, and writes one line: `MEASURED events=49 lag_samples=40 lag_p50_s=… lag_p95_s=… lag_max_s=…`. It
fails if the p95 is over 10 s, a bound for a daemon that has stopped, not a target. Six runs on 2026-10-02 gave
p95 of 0.68 to 1.15 s.

For the deployed API, the histogram goes to Application Insights. Not verified: the Azure Monitor exporter is
expected to send a histogram's count, sum, minimum and maximum and no percentile, so a p95 could not be read
there. The mean and the maximum can be, in the resource's Logs (check the columns on first use):

```
customMetrics
| where name == "ballbank.projection.lag"
| summarize samples = sum(valueCount), mean_s = sum(valueSum) / sum(valueCount), max_s = max(valueMax)
```

If a p95 is wanted from the deployed API, point the OTLP exporter at a backend that keeps buckets
(Grafana Cloud, [architecture](architecture.md#observability)) and say so here.

### Events written

The same test counts them: `session.Events.QueryAllRawEvents().CountAsync()` for the league's tenant.

### Test suites

- Locally on 2026-10-02 (Windows, Docker Desktop): `dotnet test`: domain 331 tests in 0.7 s, specs 117 in
  0.7 s (13 skipped: they are tagged `@http` and run only over HTTP), integration 441 in 5 min 5 s; 5 min 13 s
  for the whole command.
- Specs over HTTP (`BALLBANK_SPECS_DRIVER=http dotnet test tests/BallBank.Specs`), locally: 117 of 117 in 45 s.
- In CI, the same four runs are the steps *Test* and *Specs over HTTP* of the `.NET build and test` job:
  `gh run view <run id> --log | grep -E "Passed!|Failed!"`.

## Measuring by hand

These need a deployed API, a database and a person. Tokens and league ids go on the command line or into
shell variables, never into the repo. [Issue #80](https://github.com/Niewski/BallBank/issues/80) is the
checklist; record each result in the table above with the date and what it ran against.

k6 is not installed with the repo. Install it (<https://grafana.com/docs/k6/latest/set-up/install-k6/>) or run
the script through Docker, for example
`docker run --rm -i -e BASE_URL=… -e TOKEN=… -e LEAGUE=… grafana/k6 run - < k6/confirm-payment.js`.

### Cold start

The first request after the API has been idle 15 minutes, which wakes the Container Apps replica and the
Neon compute. A request that reads the database is what measures both; `GET /v1/version` measures the
replica alone. The hourly tick (minute 0, UTC) wakes Neon, so samples are taken at 20 and 40 minutes past the
hour, when it is long asleep. Ten samples take about five hours.

```
API=https://<api host>; LEAGUE=<league id>; SEASON=2026   # TOKEN: a member's bearer token, in the shell only
for i in $(seq 10); do
  until [ "$(date -u +%M)" = 20 ] || [ "$(date -u +%M)" = 40 ]; do sleep 20; done
  curl -s -o /dev/null -w '%{time_total}\n' -H "Authorization: Bearer $TOKEN" \
       "$API/leagues/$LEAGUE/seasons/$SEASON/ledger" >> cold-start.txt
  sleep 90
done
sort -n cold-start.txt | sed -n '5,6p'     # the p50 is the mean of these two
```

Take nothing else off the API in those windows (no uptime probe, no one using the app). Record the p50, the
slowest sample and the date.

### Warm latency

`k6/confirm-payment.js` attests $1 and confirms it, five times a second for two minutes, on ten accounts of
a season at a time, and reports the confirmation on a line of its own. **It writes**: 600 iterations add
1,200 events and $600 to the pot, so point it at a scratch league (ten or more members with dues assessed in
an open season, Discord not connected, nobody opted in to texts), never the league's own. The token is a
treasurer's of that league.

```
k6 run -e BASE_URL=https://<api host> -e TOKEN=<treasurer's bearer token> -e LEAGUE=<scratch league id> k6/confirm-payment.js
```

Read `http_req_duration{name:confirm}`: `med` is the p50 and `p(95)` the p95. The script's setup reads the
ledger first, so it measures a warm API. The run must show `http_req_failed` and `checks` at 0% and 100%; a
run with failures is not a measurement. Options (`RPS`, `DURATION`, `VUS`, `SEASON`) are in the script's header.

### Rate limit proof

`k6/rate-limit.js` drives one league past its budget while a second is called at the same moment. The token
must belong to a member of both leagues. It is meant to be refused, so use two scratch leagues.

```
k6 run -e BASE_URL=https://<api host> -e TOKEN=<bearer token> -e NOISY_LEAGUE=<league id> -e QUIET_LEAGUE=<league id> k6/rate-limit.js
```

The proof is `noisy_refused` above zero with the noisy league's `Retry-After`, and `quiet_refused` at zero.

### Projection lag, deployed

The query above, a minute or two after the warm-latency run (metrics go out every minute), when there
is something to read. Record the mean and maximum and say they are not a p95.

### Availability, deploys, events, bill

At the end of each month: the monitor's report; the `gh run list` command in the table; the event count query
in the table, against the Neon database (a connection string from the platform, never pasted here); and the
month's Azure, Neon and Twilio invoices, by service, next to the architecture's expected bill.
