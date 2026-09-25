"use client";

import { useAuth0 } from "@auth0/auth0-react";
import Link from "next/link";
import { useEffect, useState } from "react";
import { apiBaseUrl } from "@/lib/config";

type MyLeague = {
  leagueId: string;
  name: string;
  season: string;
  memberId: string;
  roles: string[];
};

type LeaguesState =
  | { kind: "loading" }
  | { kind: "ok"; leagues: MyLeague[] }
  | { kind: "error"; message: string };

export function MyLeagues() {
  const { getAccessTokenSilently } = useAuth0();
  const [state, setState] = useState<LeaguesState>({ kind: "loading" });

  useEffect(() => {
    const controller = new AbortController();

    getAccessTokenSilently()
      .then((token) =>
        fetch(`${apiBaseUrl}/me/leagues`, {
          headers: { Authorization: `Bearer ${token}` },
          signal: controller.signal,
        }),
      )
      .then(async (response) => {
        if (!response.ok) {
          throw new Error(`API responded ${response.status}`);
        }
        return (await response.json()) as MyLeague[];
      })
      .then((leagues) => setState({ kind: "ok", leagues }))
      .catch((error: unknown) => {
        if (controller.signal.aborted) return;
        setState({
          kind: "error",
          message: error instanceof Error ? error.message : "Unknown error",
        });
      });

    return () => controller.abort();
  }, [getAccessTokenSilently]);

  return (
    <section className="flex flex-col gap-4">
      <h2 className="text-2xl font-semibold tracking-tight text-zinc-950 dark:text-zinc-50">
        My leagues
      </h2>

      {state.kind === "loading" && (
        <p className="text-zinc-500">Loading your leagues…</p>
      )}

      {state.kind === "error" && (
        <p className="text-rose-700 dark:text-rose-400">
          Could not load your leagues: {state.message}
        </p>
      )}

      {state.kind === "ok" && state.leagues.length === 0 && (
        <div className="flex flex-col gap-3 rounded-lg border border-zinc-200 bg-white p-5 dark:border-zinc-800 dark:bg-zinc-950">
          <p className="text-zinc-700 dark:text-zinc-300">
            You are not a member of any league yet.
          </p>
          <p className="text-zinc-600 dark:text-zinc-400">
            Keeping the books for your league? Import it from Sleeper. Paying
            dues in someone else&apos;s? Ask your treasurer for an invite link.
          </p>
          <Link
            href="/leagues/import"
            className="self-start rounded-md bg-emerald-700 px-4 py-2 text-sm font-medium text-white hover:bg-emerald-800"
          >
            Import a league
          </Link>
        </div>
      )}

      {state.kind === "ok" && state.leagues.length > 0 && (
        <ul className="flex flex-col divide-y divide-zinc-200 rounded-lg border border-zinc-200 bg-white dark:divide-zinc-800 dark:border-zinc-800 dark:bg-zinc-950">
          {state.leagues.map((league) => (
            <li key={league.leagueId}>
              <Link
                href={`/league?id=${league.leagueId}`}
                className="flex items-baseline justify-between gap-4 p-4 hover:bg-zinc-50 dark:hover:bg-zinc-900"
              >
                <span className="font-medium text-zinc-950 dark:text-zinc-50">
                  {league.name}
                </span>
                <span className="text-sm text-zinc-500">
                  {league.season}
                  {league.roles.length > 0 && ` · ${league.roles.join(", ")}`}
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
