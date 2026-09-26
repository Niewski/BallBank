"use client";

import { useAuth0 } from "@auth0/auth0-react";
import { useEffect, useState } from "react";
import { Ledger } from "@/components/ledger";
import { OpenSeason } from "@/components/open-season";
import { apiBaseUrl } from "@/lib/config";
import { problemMessage, unreachableMessage } from "@/lib/problem";

type SeasonSummary = {
  seasonId: string;
  label: string;
  status: string;
  duesAmount: number;
  dueDate: string;
};

type SeasonState =
  | { kind: "loading" }
  | { kind: "ok"; seasons: SeasonSummary[] }
  | { kind: "error"; message: string };

// The league page's season section: a treasurer opens a season when none is
// open yet; once one is, its dues, due date and ledger show for everyone.
export function Season({
  leagueId,
  yourMemberId,
  youAreTreasurer,
}: {
  leagueId: string;
  yourMemberId: string | null;
  youAreTreasurer: boolean;
}) {
  const { getAccessTokenSilently } = useAuth0();
  const [state, setState] = useState<SeasonState>({ kind: "loading" });
  // Bumped to read the seasons again, as after opening one.
  const [reads, setReads] = useState(0);

  useEffect(() => {
    const controller = new AbortController();

    getAccessTokenSilently()
      .then((token) =>
        fetch(`${apiBaseUrl}/leagues/${encodeURIComponent(leagueId)}/seasons`, {
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
          kind: "ok",
          seasons: (await response.json()) as SeasonSummary[],
        });
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return;
        setState({ kind: "error", message: unreachableMessage(error) });
      });

    return () => controller.abort();
  }, [getAccessTokenSilently, leagueId, reads]);

  if (state.kind === "loading") {
    return <p className="text-sm text-zinc-500">Loading the season…</p>;
  }

  if (state.kind === "error") {
    return (
      <p className="text-sm text-rose-700 dark:text-rose-400">
        {state.message}
      </p>
    );
  }

  // The API lists most recently opened first; only one is ever open today.
  const open = state.seasons[0] ?? null;

  if (!open) {
    return youAreTreasurer ? (
      <OpenSeason leagueId={leagueId} onOpened={() => setReads((n) => n + 1)} />
    ) : (
      <p className="text-sm text-zinc-500">No season open yet.</p>
    );
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="rounded-lg border border-zinc-200 bg-white p-4 text-sm dark:border-zinc-800 dark:bg-zinc-950">
        <p className="font-medium text-zinc-950 dark:text-zinc-50">
          {open.label} season
        </p>
        <p className="text-zinc-600 dark:text-zinc-400">
          Dues ${open.duesAmount.toFixed(2)}, due{" "}
          {new Date(open.dueDate).toLocaleDateString(undefined, {
            month: "short",
            day: "numeric",
            year: "numeric",
            timeZone: "UTC",
          })}
        </p>
      </div>
      <Ledger
        leagueId={leagueId}
        season={open.label}
        yourMemberId={yourMemberId}
        youAreTreasurer={youAreTreasurer}
      />
    </div>
  );
}
