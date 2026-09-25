"use client";

import { useAuth0 } from "@auth0/auth0-react";
import { type FormEvent, useState } from "react";
import { apiBaseUrl } from "@/lib/config";

type SleeperLeagueChoice = {
  sleeperLeagueId: string;
  name: string;
  season: string;
  teams: number;
};

type PickerState =
  | { kind: "idle" }
  | { kind: "loading" }
  | { kind: "ok"; username: string; leagues: SleeperLeagueChoice[] }
  | { kind: "error"; message: string };

// The API explains 404 (no such Sleeper user) and 502 (Sleeper not answering)
// in the problem's detail; anything else gets a generic message.
async function problemMessage(response: Response): Promise<string> {
  try {
    const problem = (await response.json()) as { detail?: string };
    if (problem.detail) return problem.detail;
  } catch {
    // Not a problem document; fall through.
  }
  return `Something went wrong (API responded ${response.status}). Try again.`;
}

export function SleeperLeaguePicker() {
  const { getAccessTokenSilently } = useAuth0();
  const [username, setUsername] = useState("");
  const [state, setState] = useState<PickerState>({ kind: "idle" });

  async function findLeagues(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const name = username.trim();
    if (!name) return;

    setState({ kind: "loading" });

    try {
      const token = await getAccessTokenSilently();
      const response = await fetch(
        `${apiBaseUrl}/sleeper/users/${encodeURIComponent(name)}/leagues`,
        { headers: { Authorization: `Bearer ${token}` } },
      );

      if (!response.ok) {
        setState({ kind: "error", message: await problemMessage(response) });
        return;
      }

      const leagues = (await response.json()) as SleeperLeagueChoice[];
      setState({ kind: "ok", username: name, leagues });
    } catch (error: unknown) {
      setState({
        kind: "error",
        message:
          error instanceof Error
            ? `Could not reach BallBank: ${error.message}`
            : "Could not reach BallBank.",
      });
    }
  }

  return (
    <section className="flex flex-col gap-6">
      <form onSubmit={findLeagues} className="flex flex-col gap-2">
        <label
          htmlFor="sleeper-username"
          className="text-sm font-medium text-zinc-700 dark:text-zinc-300"
        >
          Your Sleeper username
        </label>
        <div className="flex gap-3">
          <input
            id="sleeper-username"
            name="username"
            autoComplete="off"
            autoCapitalize="none"
            spellCheck={false}
            value={username}
            onChange={(event) => setUsername(event.target.value)}
            className="min-w-0 flex-1 rounded-md border border-zinc-300 bg-white px-3 py-2 text-zinc-950 dark:border-zinc-700 dark:bg-zinc-950 dark:text-zinc-50"
          />
          <button
            type="submit"
            disabled={state.kind === "loading" || !username.trim()}
            className="rounded-md bg-emerald-700 px-4 py-2 text-sm font-medium text-white hover:bg-emerald-800 disabled:opacity-50"
          >
            Find my leagues
          </button>
        </div>
      </form>

      <div aria-live="polite">
        {state.kind === "loading" && (
          <p className="text-zinc-500">Asking Sleeper…</p>
        )}

        {state.kind === "error" && (
          <p className="text-rose-700 dark:text-rose-400">{state.message}</p>
        )}

        {state.kind === "ok" && state.leagues.length === 0 && (
          <p className="text-zinc-700 dark:text-zinc-300">
            {state.username} is not in any NFL league on Sleeper this season.
          </p>
        )}

        {state.kind === "ok" && state.leagues.length > 0 && (
          <div className="flex flex-col gap-3">
            <p className="text-sm text-zinc-600 dark:text-zinc-400">
              Your leagues on Sleeper this season:
            </p>
            <ul className="flex flex-col divide-y divide-zinc-200 rounded-lg border border-zinc-200 bg-white dark:divide-zinc-800 dark:border-zinc-800 dark:bg-zinc-950">
              {state.leagues.map((league) => (
                <li
                  key={league.sleeperLeagueId}
                  className="flex items-baseline justify-between gap-4 p-4"
                >
                  <span className="font-medium text-zinc-950 dark:text-zinc-50">
                    {league.name}
                  </span>
                  <span className="text-sm text-zinc-500">
                    {league.season} · {league.teams} teams
                  </span>
                </li>
              ))}
            </ul>
          </div>
        )}
      </div>
    </section>
  );
}
