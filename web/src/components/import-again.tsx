"use client";

import { useAuth0 } from "@auth0/auth0-react";
import { useState } from "react";
import { apiBaseUrl } from "@/lib/config";
import { problemMessage, unreachableMessage } from "@/lib/problem";

type ImportAgainState =
  | { kind: "idle" }
  | { kind: "importing" }
  | { kind: "done"; message: string }
  | { kind: "error"; message: string };

// A treasurer imports the league again after a team joins on Sleeper. Only new
// teams become members; nobody already in the league changes.
export function ImportAgain({
  leagueId,
  sleeperLeagueId,
  onImported,
}: {
  leagueId: string;
  sleeperLeagueId: string;
  onImported: () => void;
}) {
  const { getAccessTokenSilently } = useAuth0();
  const [state, setState] = useState<ImportAgainState>({ kind: "idle" });

  async function importAgain() {
    setState({ kind: "importing" });

    try {
      const token = await getAccessTokenSilently();
      const response = await fetch(
        `${apiBaseUrl}/leagues/${encodeURIComponent(leagueId)}/import`,
        {
          method: "POST",
          headers: {
            Authorization: `Bearer ${token}`,
            "Content-Type": "application/json",
          },
          body: JSON.stringify({ sleeperLeagueId }),
        },
      );

      if (!response.ok) {
        setState({ kind: "error", message: await problemMessage(response) });
        return;
      }

      const { membersAdded } = (await response.json()) as {
        membersAdded: number;
      };
      setState({
        kind: "done",
        message:
          membersAdded === 0
            ? "No new teams on Sleeper."
            : `Added ${membersAdded} new ${membersAdded === 1 ? "member" : "members"}.`,
      });
      onImported();
    } catch (error: unknown) {
      setState({ kind: "error", message: unreachableMessage(error) });
    }
  }

  return (
    <div className="flex flex-col items-start gap-2 sm:items-end">
      <button
        type="button"
        onClick={importAgain}
        disabled={state.kind === "importing"}
        title="Adds teams that joined on Sleeper since the last import"
        className="rounded-md border border-zinc-300 px-4 py-2 text-sm font-medium text-zinc-700 hover:bg-zinc-100 disabled:opacity-50 dark:border-zinc-700 dark:text-zinc-300 dark:hover:bg-zinc-900"
      >
        {state.kind === "importing" ? "Importing…" : "Import again"}
      </button>
      <div aria-live="polite" className="text-sm">
        {state.kind === "done" && (
          <p className="text-zinc-600 dark:text-zinc-400">{state.message}</p>
        )}
        {state.kind === "error" && (
          <p className="text-rose-700 dark:text-rose-400">{state.message}</p>
        )}
      </div>
    </div>
  );
}
