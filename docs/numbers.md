# Numbers

Measured, not estimated. Filled in as each milestone lands; updated monthly while the league is live.

| Metric | Value | Measured | How |
|---|---|---|---|
| Cold start (Container Apps 0→1 + Neon wake) | — | — | First request after 15 min idle, p50 of 10 samples |
| Warm p50 / p95, `POST …/attestations/{id}/confirm` | — | — | k6, 5 rps for 2 min |
| `LeaguePot` projection lag | — | — | Time from `PaymentConfirmed` to projection update, p95 |
| Rate limit proof | — | — | k6: 429s for one tenant while another tenant is unaffected |
| Events written this season | — | — | `select count(*) from ballbank.mt_events` |
| Availability (season) | — | — | External uptime monitor, 5 min interval |
| Deploys / rollbacks | — | — | GitHub Actions history |
| Test suites (count, runtime) | — | — | CI |
| Monthly bill | — | — | Azure + Neon + Twilio invoices, by service |
