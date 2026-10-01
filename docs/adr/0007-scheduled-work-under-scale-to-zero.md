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
  what was scheduled. The one other schedule is a second Job, Sunday 18:00 Eastern, that asks the API
  for its version so the first member of the league's busiest hour does not wait for it to start. It
  runs no tick and reads no database itself, but the API connects to Postgres as it starts, so the ping
  most likely wakes Neon too, which is not yet measured (runbook, "The Jobs").
- **What a tick does, for each league's open season** (the one opened last, as seasons cannot be
  closed yet): first it sends what was decided earlier and has not gone, which is every held
  notification whose `SendAfter` has passed and every reminder a send left `Pending`; then the week's
  digest, if one is due (below); then, from the `MemberStatement`s, it asks the domain
  (`Reminders.StageOn`) what stage each account with a balance above zero has reached, and sends that
  stage's reminder to the member; and last it purges what is old (below). "Today" is the UTC date, so a
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
- **The weekly digest** is one more notification the tick makes, to a league that connected Discord and
  turned the digest on (`LeagueNotificationSettings.PostDigest`): who still owes and how much, what the
  pot holds, and how many attestations wait for the treasurer, worded by the domain
  (`NotificationTexts.Digest`) from the same statements the reminders read. `Digests.WeekDueAt` says which
  ISO week is due, by the Eastern calendar: none before Monday 09:00 Eastern, then that week until
  Sunday night. Like a reminder it is keyed (`Discord/{league}/Digest/{year}-W{week}`) and inserted before
  it is sent, so the first tick at or after the slot posts it, a second one that week finds the key and
  does nothing, and a late tick posts it all the same. A week no tick found is not made up for. A digest
  Discord refused stays `Pending` and is sent again, worded again from what is owed then, only while its
  week is the one due and the league still wants it; otherwise it is *skipped* (`Digests.NoLongerDue`),
  since a digest is the news of its week. A league that disconnected Discord is not posted one.
- **The purge** ends each league's tick: it deletes the league's `IdempotencyRecord`s and `Notification`s
  older than the retention age, whatever their status, inside that league's own session. The age is
  `Retention:Days`, 90 unless set and refused below 14 ([ADR-0005](0005-idempotency-and-concurrency.md)),
  because deleting a notification frees its dedupe key, and a key has to hold for a week at most (a digest's
  week, a reminder's stage). It is last, so a purge that cannot run (the Job does not start the host, so a
  bad age is only noticed when it is first read, here) fails the league after its messages have gone, not
  before, and deletes nothing. Like the rest of the tick it visits only a league with an open season, so a
  league with none keeps its few records; closing seasons will have to revisit that.
- **One summary line per league** at `Information`: sent, held, skipped and failed, the digests posted (0
  or 1), and how many idempotency records and notifications were purged, scoped with the league's
  `tenant.id`. A league that throws is logged at `Error` and does not stop the others.
- **The dashboard** shows each delinquent member's last reminder and how it went (sent, then
  delivered or not as Twilio reports it, held, skipped), so the treasurer sees who has been told.

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
