"use client";

import { useAuth0 } from "@auth0/auth0-react";
import { useEffect, useState } from "react";
import { apiBaseUrl } from "@/lib/config";
import { formatTimestamp } from "@/lib/dates";
import { problemMessage, unreachableMessage } from "@/lib/problem";

// One event of the account's stream, as the API renders it.
type HistoryEntry = {
  sequence: number;
  version: number;
  at: string;
  type: string;
  by: string;
  byName: string | null;
  sentence: string;
};

type HistoryState =
  | { kind: "loading" }
  | { kind: "ok"; entries: HistoryEntry[] }
  | { kind: "error"; message: string };

// Every event of the account, oldest first, so a dispute can be settled from
// the record. Key it by the account's version to read it again after a change.
export function AccountHistory({
  leagueId,
  accountId,
}: {
  leagueId: string;
  accountId: string;
}) {
  const { getAccessTokenSilently } = useAuth0();
  const [state, setState] = useState<HistoryState>({ kind: "loading" });

  useEffect(() => {
    const controller = new AbortController();

    getAccessTokenSilently()
      .then((token) =>
        fetch(
          `${apiBaseUrl}/leagues/${encodeURIComponent(leagueId)}/accounts/${encodeURIComponent(accountId)}/history`,
          {
            headers: { Authorization: `Bearer ${token}` },
            signal: controller.signal,
          },
        ),
      )
      .then(async (response) => {
        if (!response.ok) {
          setState({ kind: "error", message: await problemMessage(response) });
          return;
        }
        setState({
          kind: "ok",
          entries: (await response.json()) as HistoryEntry[],
        });
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return;
        setState({ kind: "error", message: unreachableMessage(error) });
      });

    return () => controller.abort();
  }, [getAccessTokenSilently, leagueId, accountId]);

  return (
    <section className="flex flex-col gap-2">
      <h2 className="text-lg font-semibold text-zinc-950 dark:text-zinc-50">
        History
      </h2>
      {state.kind === "loading" ? (
        <p className="text-sm text-zinc-500">Loading the history…</p>
      ) : state.kind === "error" ? (
        <p className="text-sm text-rose-700 dark:text-rose-400">
          {state.message}
        </p>
      ) : (
        <ol className="divide-y divide-zinc-200 rounded-lg border border-zinc-200 bg-white text-sm dark:divide-zinc-800 dark:border-zinc-800 dark:bg-zinc-950">
          {state.entries.map((entry) => (
            <li key={entry.sequence} className="flex flex-col gap-0.5 p-3">
              <span className="text-zinc-950 dark:text-zinc-50">
                {entry.sentence}
              </span>
              <span className="text-xs text-zinc-500">
                {formatTimestamp(entry.at)} · #{entry.version}
              </span>
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}
