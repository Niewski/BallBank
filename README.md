# BallBank

**The books for your fantasy league's dues.** Who owes, who says they paid, who confirmed it, and who
got paid out at the end of the season — recorded as events, so the history *is* the audit trail.

[![CI](https://github.com/Niewski/BallBank/actions/workflows/ci.yml/badge.svg)](https://github.com/Niewski/BallBank/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

> Status: pre-alpha, being built in the open during the 2026 NFL season for one real league.
> Screenshots and a live demo land with the first usable release (see [docs/roadmap.md](docs/roadmap.md)).

## Why

Every season the same thread: "who still owes?", "I Venmo'd you in August", "did we ever pay out
third place?". Spreadsheets rot, group chats scroll away, and the treasurer ends up being the
database. BallBank gives the league one shared, append-only record of what happened, and gives the
treasurer a confirmation queue instead of a memory test.

BallBank **does not move money**. Members pay each other the way they already do (Venmo, Zelle,
cash); a member records *"I paid $50, Venmo ref VN-1234"*, the treasurer confirms it, and the books
update. No funds are held, no payment processing, no custody — by design ([ADR-0002](docs/adr/0002-ledger-of-record-no-custody.md)).

## What it does

- Import a league from [Sleeper](https://sleeper.com) (read-only public API) and carry it forward season to season.
- Assess dues and fees per member; every assessment is idempotent.
- Members attest payments with a rail and a reference; the treasurer confirms or rejects with a reason.
- Balances are derived from events, never stored. Statements and the league pot are projections.
- Reminders over Discord and SMS, with explicit opt-in for texts.
- Payout structure, standings, payouts and season close, with the invariant that the pot is never overdrawn.
- A full per-league history: who did what, when, and why.

## Architecture in one paragraph

An ASP.NET Core API on .NET 10 with [Marten](https://martendb.io) as the event store on PostgreSQL and
[Wolverine](https://wolverinefx.net) for command handling, HTTP endpoints, the transactional outbox and
scheduled work. Each league is a tenant (conjoined tenancy: `tenant_id` on every row). Aggregates are
pure C# in `BallBank.Domain`: they decide (command → event) and evolve (event → state), with no
package references. The web app is Next.js, exported as static files. Locally, one `aspire run`
starts PostgreSQL, the API and the web app with a dashboard for logs, traces and metrics. See
[docs/architecture.md](docs/architecture.md) and [docs/domain.md](docs/domain.md).

## Running locally

Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/download), Node.js 22+, Docker Desktop (or another
container runtime for PostgreSQL), and the [Aspire CLI](https://aspire.dev) (`irm https://aspire.dev/install.ps1 | iex`
on Windows, `curl -sSL https://aspire.dev/install.sh | bash` elsewhere).

```bash
git clone https://github.com/Niewski/BallBank.git
cd BallBank
aspire run            # PostgreSQL + API + web + dashboard
```

Without Aspire: `docker compose up -d`, then `dotnet run --project src/BallBank.Api` and
`cd web && npm install && npm run dev` with `NEXT_PUBLIC_API_URL=http://localhost:5213` in `web/.env.local`.

### Sign-in (Auth0)

Sign-in needs an Auth0 tenant of your own (the free tier is enough). Once:

1. Create an **API** with an identifier such as `https://api.ballbank.local`; that is the audience.
2. Create a **Single Page Application**. Allowed callback, logout and web origin URLs:
   `http://localhost:3000`. Under *Advanced → Grant types* keep *Authorization Code* and *Refresh Token*;
   turn on *Refresh Token Rotation*.
3. Enable the *Google* and *Username-Password-Authentication* connections for the application.
4. Store the settings in the AppHost's user-secrets (Aspire passes them to the API and the web app):

   ```bash
   cd src/BallBank.AppHost
   dotnet user-secrets set Parameters:auth0-domain your-tenant.us.auth0.com
   dotnet user-secrets set Parameters:auth0-audience https://api.ballbank.local
   dotnet user-secrets set Parameters:auth0-client-id <the SPA's client id>
   ```

   Without Aspire, set `Auth0:Domain` and `Auth0:Audience` in the API's user-secrets
   (`dotnet user-secrets set Auth0:Domain ... --project src/BallBank.Api`) and the `NEXT_PUBLIC_AUTH0_*`
   values in `web/.env.local` (see `web/.env.example`).

The integration tests do not need any of this: they sign their own tokens with a key generated at test time.

## Tests

```bash
dotnet test tests/BallBank.Domain.Tests tests/BallBank.Specs   # fast, no Docker
dotnet test                                                     # + integration tests (Testcontainers → PostgreSQL)
cd web && npm run lint && npm run build
```

Three layers, on purpose:

- **Domain tests** — given events, when command, then event (or a refusal). Pure, milliseconds.
- **Specs** — [Reqnroll](https://reqnroll.net) scenarios in league language (`tests/BallBank.Specs/Features`), the
  suite that gates a deploy.
- **Integration tests** — Marten against a real PostgreSQL: aggregation, tenancy isolation, projections;
  and the API over HTTP (`WebApplicationFactory`), with bearer tokens from a test signing key.

## Decisions

Architecture decisions are recorded in [docs/adr](docs/adr). Start with
[0001 (event sourcing with Marten)](docs/adr/0001-event-sourcing-with-marten.md) and
[0002 (ledger of record, no custody)](docs/adr/0002-ledger-of-record-no-custody.md).

## Numbers and cost

Latency, projection lag, availability and the monthly bill are published in
[docs/numbers.md](docs/numbers.md) once the league is live. The target is a bill of a few dollars a
month, almost all of it SMS.

## Contributing

Issues and PRs welcome. The repo is set up for agent-assisted development (see `CLAUDE.md`); the
rules in that file apply to humans too. Please don't submit real people's names, phone numbers or
payment references in fixtures.

## License

[MIT](LICENSE)
