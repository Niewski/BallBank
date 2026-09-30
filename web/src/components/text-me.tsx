"use client";

import { useAuth0 } from "@auth0/auth0-react";
import { useEffect, useId, useState, type FormEvent } from "react";
import { formatPhone } from "@/components/contact-details";
import { apiBaseUrl } from "@/lib/config";
import { problemMessage, unreachableMessage } from "@/lib/problem";

type QuietHours = { startHour: number; endHour: number; timeZone: string };

type Reading = {
  consent: { phone: string; givenAt: string } | null;
  quietHours: QuietHours;
  // The number replied STOP: nothing is sent to it whatever was consented to.
  optedOut: boolean;
  optedIn: boolean;
};

const hours = Array.from({ length: 24 }, (_, hour) => hour);

// 0 as "12 am (midnight)", 21 as "9 pm".
function hourLabel(hour: number): string {
  const clock = `${hour % 12 || 12} ${hour < 12 ? "am" : "pm"}`;
  return hour === 0 ? `${clock} (midnight)` : clock;
}

// Whether BallBank may text a member, as a label. A treasurer sees this for
// every member and can change none of it: consent is the member's alone.
export function OptedIn({ optedIn }: { optedIn: boolean }) {
  return optedIn ? (
    <span className="text-emerald-700 dark:text-emerald-400">Opted in</span>
  ) : (
    <span className="text-zinc-400">Not opted in</span>
  );
}

// The member's own texting choices: the opt-in, with what it agrees to, and
// the hours to keep quiet. The API refuses anyone else, so this is only shown
// on the member's own row.
export function TextMe({
  leagueId,
  memberId,
  teamName,
  phone,
  optedIn,
  onSaved,
}: {
  leagueId: string;
  memberId: string;
  teamName: string;
  // E.164; null when none is on record, and there is then nothing to consent to.
  phone: string | null;
  optedIn: boolean;
  onSaved: () => void;
}) {
  const [editing, setEditing] = useState(false);

  if (editing) {
    return (
      <TextMeForm
        leagueId={leagueId}
        memberId={memberId}
        teamName={teamName}
        phone={phone}
        onDone={(saved) => {
          setEditing(false);
          if (saved) onSaved();
        }}
      />
    );
  }

  return (
    <div className="flex flex-col items-start gap-1">
      <OptedIn optedIn={optedIn} />
      <button
        type="button"
        onClick={() => setEditing(true)}
        aria-label={`Change texts for ${teamName}`}
        className="text-xs font-medium text-emerald-700 hover:underline dark:text-emerald-400"
      >
        {optedIn ? "Change" : "Text me"}
      </button>
    </div>
  );
}

type FormState =
  | { kind: "loading" }
  | { kind: "ready"; reading: Reading }
  | { kind: "error"; message: string };

type SaveState =
  | { kind: "idle" }
  | { kind: "saving" }
  | { kind: "error"; message: string };

function TextMeForm({
  leagueId,
  memberId,
  teamName,
  phone,
  onDone,
}: {
  leagueId: string;
  memberId: string;
  teamName: string;
  phone: string | null;
  onDone: (saved: boolean) => void;
}) {
  const { getAccessTokenSilently } = useAuth0();
  const [state, setState] = useState<FormState>({ kind: "loading" });
  const url = `${apiBaseUrl}/leagues/${encodeURIComponent(leagueId)}/members/${encodeURIComponent(memberId)}/notifications`;

  useEffect(() => {
    const controller = new AbortController();

    getAccessTokenSilently()
      .then((token) =>
        fetch(url, {
          headers: { Authorization: `Bearer ${token}` },
          signal: controller.signal,
        }),
      )
      .then(async (response) => {
        if (!response.ok) {
          setState({ kind: "error", message: await problemMessage(response) });
          return;
        }
        setState({
          kind: "ready",
          reading: (await response.json()) as Reading,
        });
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return;
        setState({ kind: "error", message: unreachableMessage(error) });
      });

    return () => controller.abort();
  }, [getAccessTokenSilently, url]);

  if (state.kind === "loading") {
    return <p className="text-xs text-zinc-500">Loading…</p>;
  }

  if (state.kind === "error") {
    return (
      <div className="flex flex-col items-start gap-1 text-xs">
        <p className="text-rose-700 dark:text-rose-400">{state.message}</p>
        <button
          type="button"
          onClick={() => onDone(false)}
          className="font-medium text-emerald-700 hover:underline dark:text-emerald-400"
        >
          Close
        </button>
      </div>
    );
  }

  return (
    <PreferencesForm
      url={url}
      teamName={teamName}
      phone={phone}
      reading={state.reading}
      onDone={onDone}
    />
  );
}

function PreferencesForm({
  url,
  teamName,
  phone,
  reading,
  onDone,
}: {
  url: string;
  teamName: string;
  phone: string | null;
  reading: Reading;
  onDone: (saved: boolean) => void;
}) {
  const { getAccessTokenSilently } = useAuth0();
  const id = useId();
  // Consent stands only at the number on record; one given at an earlier number starts unticked.
  const [textMe, setTextMe] = useState(
    phone !== null && reading.consent?.phone === phone,
  );
  const [startHour, setStartHour] = useState(reading.quietHours.startHour);
  const [endHour, setEndHour] = useState(reading.quietHours.endHour);
  const [timeZone, setTimeZone] = useState(reading.quietHours.timeZone);
  const [state, setState] = useState<SaveState>({ kind: "idle" });

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setState({ kind: "saving" });

    try {
      const token = await getAccessTokenSilently();
      const response = await fetch(url, {
        method: "PUT",
        headers: {
          Authorization: `Bearer ${token}`,
          "Content-Type": "application/json",
        },
        // Replaces what was kept: opting out is saving with the box cleared.
        body: JSON.stringify({
          textMe,
          quietHours: { startHour, endHour, timeZone: timeZone.trim() },
        }),
      });

      if (!response.ok) {
        setState({ kind: "error", message: await problemMessage(response) });
        return;
      }

      onDone(true);
    } catch (error: unknown) {
      setState({ kind: "error", message: unreachableMessage(error) });
    }
  }

  const field =
    "min-w-0 rounded-md border border-zinc-300 bg-white px-2 py-1 text-zinc-950 dark:border-zinc-700 dark:bg-zinc-950 dark:text-zinc-50";
  const saving = state.kind === "saving";
  const consentId = `${id}-consent`;
  const zonesId = `${id}-zones`;

  return (
    <form
      onSubmit={save}
      aria-label={`Texts for ${teamName}`}
      className="flex min-w-64 max-w-72 flex-col gap-3"
    >
      {phone ? (
        <div className="flex flex-col gap-1">
          <label className="flex items-start gap-2 text-sm font-medium text-zinc-900 dark:text-zinc-100">
            <input
              type="checkbox"
              name="textMe"
              checked={textMe}
              onChange={(event) => setTextMe(event.target.checked)}
              aria-describedby={consentId}
              className="mt-1"
            />
            Text me at {formatPhone(phone)}
          </label>
          <p id={consentId} className="text-xs text-zinc-500">
            BallBank will text this number when you are assessed dues, when a
            payment of yours is confirmed or rejected, when an adjustment is
            posted to your account, and to remind you as a due date nears.
            Message and data rates may apply. Reply STOP to any message to stop
            them.
          </p>
          {reading.optedOut && (
            <p className="text-xs text-amber-700 dark:text-amber-400">
              This number asked BallBank to stop texting it, so nothing is sent
              to it for now.
            </p>
          )}
        </div>
      ) : (
        <p className="text-xs text-zinc-500">
          Texts go to a specific number. Add your phone number in your contact
          details first.
        </p>
      )}

      <fieldset className="flex flex-col gap-2">
        <legend className="text-xs font-medium text-zinc-700 dark:text-zinc-300">
          Quiet hours
        </legend>
        <p className="text-xs text-zinc-500">
          A text that falls inside them waits until they end.
        </p>
        <div className="flex items-center gap-2">
          <label className="flex flex-col gap-0.5 text-xs text-zinc-500">
            From
            <select
              name="startHour"
              value={startHour}
              onChange={(event) => setStartHour(Number(event.target.value))}
              className={field}
            >
              {hours.map((hour) => (
                <option key={hour} value={hour}>
                  {hourLabel(hour)}
                </option>
              ))}
            </select>
          </label>
          <label className="flex flex-col gap-0.5 text-xs text-zinc-500">
            Until
            <select
              name="endHour"
              value={endHour}
              onChange={(event) => setEndHour(Number(event.target.value))}
              className={field}
            >
              {hours.map((hour) => (
                <option key={hour} value={hour}>
                  {hourLabel(hour)}
                </option>
              ))}
            </select>
          </label>
        </div>
        <label className="flex flex-col gap-0.5 text-xs text-zinc-500">
          Time zone
          <input
            name="timeZone"
            list={zonesId}
            autoComplete="off"
            autoCapitalize="none"
            spellCheck={false}
            required
            placeholder="America/New_York"
            value={timeZone}
            onChange={(event) => setTimeZone(event.target.value)}
            className={field}
          />
          <datalist id={zonesId}>
            <option value="America/New_York" />
            <option value="America/Chicago" />
            <option value="America/Denver" />
            <option value="America/Phoenix" />
            <option value="America/Los_Angeles" />
            <option value="America/Anchorage" />
            <option value="Pacific/Honolulu" />
          </datalist>
        </label>
      </fieldset>

      <div className="flex gap-2">
        <button
          type="submit"
          disabled={saving}
          className="rounded-md bg-emerald-700 px-3 py-1 text-xs font-medium text-white hover:bg-emerald-800 disabled:opacity-50"
        >
          {saving ? "Saving…" : "Save"}
        </button>
        <button
          type="button"
          disabled={saving}
          onClick={() => onDone(false)}
          className="rounded-md px-3 py-1 text-xs font-medium text-zinc-700 hover:bg-zinc-100 disabled:opacity-50 dark:text-zinc-300 dark:hover:bg-zinc-900"
        >
          Cancel
        </button>
      </div>
      <div aria-live="polite" className="text-xs">
        {state.kind === "error" && (
          <p className="text-rose-700 dark:text-rose-400">{state.message}</p>
        )}
      </div>
    </form>
  );
}
