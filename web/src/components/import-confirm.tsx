"use client";

import { useAuth0 } from "@auth0/auth0-react";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { apiBaseUrl } from "@/lib/config";
import { problemMessage, unreachableMessage } from "@/lib/problem";

export type SleeperLeagueChoice = {
  sleeperLeagueId: string;
  name: string;
  season: string;
  teams: number;
};

type ConfirmState =
  | { kind: "idle" }
  | { kind: "importing" }
  | { kind: "error"; message: string };

// The last look before importing: which league, which season, how many teams.
// Importing makes the signed-in person its treasurer, then "My leagues" shows it.
export function ImportConfirm({
  league,
  username,
  onCancel,
}: {
  league: SleeperLeagueChoice;
  username: string;
  onCancel: () => void;
}) {
  const { getAccessTokenSilently, user } = useAuth0();
  const router = useRouter();
  const [state, setState] = useState<ConfirmState>({ kind: "idle" });
  // Chosen once, so pressing Import again after a failure retries the same league.
  const [leagueId] = useState(() => crypto.randomUUID());

  async function importLeague() {
    setState({ kind: "importing" });

    try {
      const token = await getAccessTokenSilently();
      const response = await fetch(`${apiBaseUrl}/leagues/${leagueId}/import`, {
        method: "POST",
        headers: {
          Authorization: `Bearer ${token}`,
          "Content-Type": "application/json",
        },
        body: JSON.stringify({
          sleeperLeagueId: league.sleeperLeagueId,
          sleeperUsername: username,
          displayName: user?.name ?? user?.nickname ?? username,
        }),
      });

      if (!response.ok) {
        setState({ kind: "error", message: await problemMessage(response) });
        return;
      }

      router.push("/");
    } catch (error: unknown) {
      setState({ kind: "error", message: unreachableMessage(error) });
    }
  }

  return (
    <section className="flex flex-col gap-4 rounded-lg border border-zinc-200 bg-white p-5 dark:border-zinc-800 dark:bg-zinc-950">
      <div className="flex flex-col gap-1">
        <h2 className="text-xl font-semibold text-zinc-950 dark:text-zinc-50">
          {league.name}
        </h2>
        <p className="text-sm text-zinc-500">
          {league.season} season · {league.teams} teams
        </p>
      </div>
      <p className="text-zinc-600 dark:text-zinc-400">
        Every team becomes a member, and you become the league&apos;s treasurer.
      </p>

      <div className="flex gap-3">
        <button
          type="button"
          onClick={importLeague}
          disabled={state.kind === "importing"}
          className="rounded-md bg-emerald-700 px-4 py-2 text-sm font-medium text-white hover:bg-emerald-800 disabled:opacity-50"
        >
          {state.kind === "importing" ? "Importing…" : "Import"}
        </button>
        <button
          type="button"
          onClick={onCancel}
          disabled={state.kind === "importing"}
          className="rounded-md px-4 py-2 text-sm font-medium text-zinc-700 hover:bg-zinc-100 disabled:opacity-50 dark:text-zinc-300 dark:hover:bg-zinc-900"
        >
          Pick another
        </button>
      </div>

      <div aria-live="polite">
        {state.kind === "error" && (
          <p className="text-rose-700 dark:text-rose-400">{state.message}</p>
        )}
      </div>
    </section>
  );
}
