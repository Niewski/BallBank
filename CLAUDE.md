# BallBank

Keeps the books for a fantasy league's dues: who owes what, who says they paid, who confirmed it,
and who got paid out at season close. An event-sourced ledger. Money never moves through BallBank;
it records payments members make to each other (ADR-0002).

Read `CONTEXT.md` before naming anything. Read the ADR for any area you touch (`docs/adr/`).

## Stack

.NET 10 · ASP.NET Core · Marten (event store + documents on PostgreSQL) · Wolverine (handlers, HTTP
endpoints, transactional outbox, scheduled messages) · Aspire (local orchestration and telemetry)
· Next.js 16 / React 19 / Tailwind (`web/`, static export) · xUnit + Shouldly · Reqnroll (Gherkin)
· Testcontainers · OpenTelemetry.

## Commands

| Task | Command |
|---|---|
| Run everything locally (Postgres, API, web, dashboard) | `aspire run` (or `dotnet run --project src/BallBank.AppHost`) |
| Build all .NET | `dotnet build` |
| Fast tests, no Docker | `dotnet test tests/BallBank.Domain.Tests tests/BallBank.Specs` |
| All .NET tests (integration tests start PostgreSQL via Testcontainers; Docker required) | `dotnet test` |
| Web | `cd web && npm run dev` / `npm run lint` / `npm run build` |
| Postgres without Aspire | `docker compose up -d` |

CI (`.github/workflows/ci.yml`) runs build, all .NET tests, web lint and web build on every PR.

## Layout

```
src/BallBank.Domain          aggregates, events, commands, invariants — no package references
src/BallBank.Api             Wolverine.HTTP endpoints (vertical slices under Features/), Marten config,
                             tenancy, idempotency, rate limiting, integrations (Sleeper, Discord, SMS)
src/BallBank.ServiceDefaults OpenTelemetry, health checks, resilience (Aspire pattern)
src/BallBank.AppHost         Aspire orchestration
web/                         Next.js app
tests/BallBank.Domain.Tests  given(events).when(command).then(event | DomainException)
tests/BallBank.Integration.Tests   Marten + PostgreSQL via Testcontainers
tests/BallBank.Specs         Reqnroll features in league language
docs/                        architecture, domain, roadmap, numbers, runbook, adr/
```

## Rules that do not bend

1. `BallBank.Domain` stays free of packages. Aggregates **decide** (validate a command, return an
   event or throw `DomainException`) and **evolve** (`Evolve`, backed by private `When` methods;
   never name them `Apply`/`Create`, which Marten 9 claims by convention). No I/O, no clocks: `now` is a parameter.
2. Events are immutable facts in the past tense. Never edit a released event type; add a new one.
3. Balances are derived from events, never stored.
4. Every command carries the id of the fact it creates; replaying a command is a no-op (`null`).
   Every mutating HTTP call will carry an `Idempotency-Key` and an expected `Version`.
5. The tenant (league) is resolved from route + token, never from the request body.
6. New domain behaviour gets a domain test first. New user-facing flow gets a Reqnroll scenario.
7. Nothing in the repo holds a secret or personal data: no connection strings with passwords, no
   tokens, no phone numbers, no member names beyond the fixtures. Local secrets go in
   `dotnet user-secrets`; deployed secrets live in the platform.
8. Prefer the smallest change that makes the scenario pass; refactor after green.

## Working with the skills

The engineering skills from `mattpocock/skills` are installed under `.claude/skills/`
(`skills-lock.json` pins them; `npx skills update` refreshes). Their per-repo configuration is
already written in `docs/agents/` — run `/setup-matt-pocock-skills` once to confirm it.

- New feature or change: `/grill-with-docs` → `/to-spec` (or `/to-tickets` for several slices) →
  `/implement` (runs `/tdd` and `/code-review` as gates).
- Bug: `/diagnosing-bugs`.
- Something to explore before committing to it: `/prototype`.
- Naming or vocabulary question: `/domain-modeling` (updates `CONTEXT.md`).
- Every few days: `/improve-codebase-architecture`.
- Not sure which: `/ask-matt`.

## Agent skills

### Issue tracker

Issues live in this repository's GitHub Issues (`gh` CLI). See `docs/agents/issue-tracker.md`.

### Triage labels

Default vocabulary: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`.
See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: `CONTEXT.md` at the root, decisions in `docs/adr/`. See `docs/agents/domain.md`.
