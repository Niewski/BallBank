# ADR-0007: Scheduled work under scale-to-zero

- **Status:** Accepted
- **Date:** 2026-09-24 (accepted 2026-10-01)

## Context

The API runs on a consumption plan that scales to zero. Wolverine's scheduled messages fire from
inside the process, so a reminder due at 09:00 fires at 09:00 only if something is awake.

Keeping one replica warm at the smallest size (0.25 vCPU) costs roughly 648,000 vCPU-seconds a
month, well past the free grant of 180,000, and would be the largest line on the bill.

## Decision

Scheduled work runs from a Container Apps **Job** on a cron schedule, built from the same image and
invoking a `tick` command that does what is due and exits. The API itself stays scale-to-zero.
Wolverine scheduling is still used for short-horizon, in-flight work (retries, delayed follow-ups)
that happens while the app is awake.

As built:

- **The command.** `dotnet BallBank.Api.dll tick` is a JasperFx command (`TickCommand`) in the API
  assembly. It builds the host and does not start it: no Kestrel, no projection daemon, no Wolverine
  listeners, so a Job never competes with the API for the work only its node owns. It exits `0`, or
  non-zero when a league could not be finished or a send was refused, so the Job shows as failed. The
  API's entry point is unchanged. `Tick` is the service doing the work, with the injected `TimeProvider`, so a test runs a
  tick as of any time.
- **Cadence.** Hourly, on the hour. Reminders are sent as of the tick that finds them due, so the
  hour is how late one can be; quiet hours are whole hours, so a held text goes out within an hour of
  them ending. A tick that did not run (an outage, a failed Job) loses nothing: the next one finds
  the same things due, because what is due is worked out from the books and the clock, not from
  what was scheduled.
- **What a tick does, for each league's open season** (the one opened last, as seasons cannot be
  closed yet): first it sends what was decided earlier and has not gone, which is every held
  notification whose `SendAfter` has passed and every reminder a send left `Pending`; then, from the
  `MemberStatement`s, it asks the domain (`Reminders.StageOn`) what stage each account with a balance
  above zero has reached, and sends that stage's reminder to the member. "Today" is the UTC date, so a
  member in the Americas can be told "due today" on the evening before, by their own clock; quiet hours,
  which are by the member's clock, mean such a text is usually held until the morning.
- **A reminder goes with what is owed when it goes.** A reminder that was held, or refused and left
  pending, is checked against the statement again before it is sent: a member who has paid up since is
  *skipped* (`Reminders.PaidUp`), and otherwise the text is worded again with the current balance.
- **The stages** are three days before the earliest due date of what the member was assessed, on the
  day, and every whole week overdue after it (7, 14, 21 days …). `StageOn` answers the *latest* stage
  reached, not the one the day falls on, so a missed tick is caught up by the next, and only the
  latest stage is sent: a member three weeks behind is told once, not three times.
- **Once, however often.** A reminder is a `Notification` keyed on member, account, due date and
  stage. The tick inserts the notification before it sends, and the insert claims the key, so a
  second tick, a retry, or a stage already sent finds it and does nothing. Paying up (a confirmed
  payment, an adjustment) removes the balance and so the reminders; an attestation waiting for the
  treasurer does not, since only a confirmation moves the balance. The stage is part of the key, which
  is built from its wording (`ReminderStage.ToString()`), so that wording does not change.
- **Consent and quiet hours** are the same decision as for every text (`SmsDelivery.Decide`), made
  when the text goes: a member who has not opted in is *skipped*, one in their quiet hours is
  *held*, and a later tick releases it. A skipped reminder has still claimed its key, so it is not
  sent later if the member opts in; they are reminded at the next stage.
- **One tick at a time.** A Postgres session advisory lock serializes ticks. A tick that finds another
  running logs it and does nothing, since two at once could each find a reminder pending and both send
  it. A Job whose previous run overran is therefore harmless. The lock belongs to one
  connection, taken from the store's connection string, so the Job's string must reach Postgres
  directly: behind a transaction pooler the lock and its release can land on different server
  connections.
- **Delivery is inline.** A process that exits cannot keep draining the durable `notifications` queue,
  so the tick calls the same `SendNotificationHandler` the queue does and records the outcome. A send
  the channel refuses leaves the notification `Pending`, counted as *failed*, and the next tick tries
  again. It is not dead-lettered, as nothing carries it. A process that dies between the channel
  accepting a text and the tick recording it sends that text again on the next tick, the duplicate
  ADR-0008 already accepts of Twilio's retries.
- **One summary line per league** at `Information`: sent, held, skipped and failed, scoped with the
  league's `tenant.id`. A league that throws is logged at `Error` and does not stop the others.
- **The dashboard** shows each delinquent member's last reminder and how it went (sent, held,
  skipped), so the treasurer sees who has been told.

The weekly delinquency digest is not built; it will be one more thing the tick does.

## Alternatives

- **`minReplicas: 1`.** Simple, and the most expensive thing in the system.
- **GitHub Actions cron hitting an authenticated endpoint.** Free and adequate, but schedules drift
  under load and are disabled on inactive repositories.
- **Wolverine scheduled messages set at assessment time.** A reminder is scheduled for each date up
  front, and fires only if the process is awake then. A change of due date or a payment would also
  have to cancel them. Deriving what is due from the books at tick time has neither problem.

## Consequences

Two entry points in one image; reminder logic is idempotent across ticks because its keys are.
Every tick reads every open league's statements, which a few leagues of a dozen members make
trivial; a larger deployment would have to page it. The Job needs the same connection string and
settings as the API (the Twilio credentials, `Notifications:WebBaseUrl`).
