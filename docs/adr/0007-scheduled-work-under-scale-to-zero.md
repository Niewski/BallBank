# ADR-0007: Scheduled work under scale-to-zero

- **Status:** Proposed
- **Date:** 2026-09-24

## Context

The API runs on a consumption plan that scales to zero. Wolverine's scheduled messages fire from
inside the process, so a reminder due at 09:00 fires at 09:00 only if something is awake.

Keeping one replica warm at the smallest size (0.25 vCPU) costs roughly 648,000 vCPU-seconds a
month, well past the free grant of 180,000, and would be the largest line on the bill.

## Decision (proposed)

Scheduled work runs from a Container Apps **Job** on a cron schedule, built from the same image and
invoking a `tick` entry point that sends due reminders and the weekly digest, then exits. The API
itself stays scale-to-zero. Wolverine scheduling is still used for short-horizon, in-flight work
(retries, delayed follow-ups) that happens while the app is awake.

## Alternatives

- **`minReplicas: 1`.** Simple, and the most expensive thing in the system.
- **GitHub Actions cron hitting an authenticated endpoint.** Free and adequate, but schedules drift
  under load and are disabled on inactive repositories.

## Consequences

Two entry points in one image; reminder logic must be idempotent across ticks.
