# ADR-0013: Every league has its own request budget

- **Status:** Accepted
- **Date:** 2026-10-01

## Context

Tenant = league ([ADR-0004](0004-conjoined-tenancy.md)), and one replica serves every league
([ADR-0009](0009-container-apps-and-neon.md), [ADR-0007](0007-scheduled-work-under-scale-to-zero.md)). A script gone wrong, a client retrying in a
loop or a page that polls in one league must not slow another league down. Third parties also call
the API without signing in and without a league in the route (`/webhooks/...`: Stripe, Twilio's
delivery status).

## Decision

ASP.NET Core's rate limiter, as one global partitioned limiter of token buckets, in process. It runs
after authentication, so a signed-in caller can be told apart, and before authorization, which reads
the database: a refused request costs no query.

Who spends which bucket, decided from the request alone:

| Request | Bucket |
|---|---|
| Under `/webhooks` | One per remote address |
| Under `/leagues/{leagueId}` | One per league: everyone's requests in a league spend it together. The id is read the way tenancy reads it, so spelling it in capitals buys no second bucket |
| Signed in, anywhere else (`/me/leagues`, Sleeper lookups) | One per caller, by the token's subject |
| Anything else (the version endpoint, health checks, unauthenticated outside a league) | Not limited |

Budgets are settings, `RateLimits:{League|Caller|Webhook}:{Burst|Refill|Every}`, so a season can tune
them from the platform without a release (`RateLimits__League__Burst=400`). Defaults, generous for
a league of a dozen people (a page is a handful of requests):

| Bucket | Burst | Refill |
|---|---|---|
| League | 200 | 50 every second |
| Caller | 100 | 20 every second |
| Webhook address | 50 | 10 every second |

The integration and specs hosts raise every budget far beyond what a test spends. The rate-limit
tests run on a host of their own with a budget of five requests that refills one an hour.

A refused request is `429` as problem details (`type: rate-limited`, a `detail` that says how many
seconds to wait) with `Retry-After`, which CORS exposes so the web can read it. It is counted by
`ballbank.ratelimit.rejections` (meter `BallBank.RateLimiting`), tagged `partition` and, for a league,
`tenant.id`. Callers and addresses are never tags: a subject or an address is personal data and a
tag per person is unbounded. `k6/rate-limit.js` proves it against a deployed environment, by hand:
one league drives past its budget and is refused while another is answered.

### When there is more than one replica

Buckets live in the process, so with N replicas a league's real budget is N times its setting. That
is fine while `maxReplicas` is 1. Before it is raised, the same partition keys (`league:…`,
`caller:…`, `webhook:…`) move to a shared store: a Redis-backed `PartitionedRateLimiter` built in
`RateLimiting.cs` in place of the in-process one, so nothing else changes. Redis is a paid service
and one more thing to keep up, which is why it is not there now.

## Alternatives considered

- **One budget for everything.** One noisy league starves the rest, which is the failure this exists
  to prevent. Rejected.
- **A bucket per league and caller.** Stops one member starving the others, but a league's total
  is then bounded only by how many members it has, and a caller is not known until the token is
  checked. Can be added inside a league later. Rejected for now.
- **Limiting at the edge only (a gateway or front door).** A paid tier, and it does not know which
  league a request is for. Rejected for now.
- **Dividing each budget by the replica count.** Brittle under autoscaling. Rejected.

## Consequences

- This departs from [ADR-0004](0004-conjoined-tenancy.md), which derives the tenant after
  authorization: the limiter keys on the league id in the route before the caller is known to belong
  to it. So a request that carries no valid token still spends the budget of the league in its route,
  and someone who knows a league's id (a GUID shared only with its members) can make that league's
  members wait for the refill. Accepted, because the limiter must run before authorization to protect
  the database. A per-address limit at the edge is the answer if it ever happens.
- A refused request never reached an endpoint, so it left nothing in the books and retrying a
  mutating one with the same `Idempotency-Key` is safe.
- Behind Container Apps' ingress the address the API sees is the ingress's, so every webhook would share
  one bucket. The container needs `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` for the real address
  from `X-Forwarded-For`; the ingress must stay the only way in.
- Defaults are a guess until the rate-limit measurement in [numbers.md](../numbers.md) is made.
