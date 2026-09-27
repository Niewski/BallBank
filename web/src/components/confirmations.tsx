"use client";

import { useAuth0 } from "@auth0/auth0-react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useEffect, useState } from "react";
import { apiBaseUrl } from "@/lib/config";
import { formatTimestamp } from "@/lib/dates";
import { formatMoney } from "@/lib/money";
import { problemMessage, unreachableMessage } from "@/lib/problem";
import { SettleAttestation } from "./settle-attestation";

type ConfirmationQueueEntry = {
  accountId: string;
  attestationId: string;
  memberId: string;
  teamName: string;
  displayName: string | null;
  amount: number;
  rail: string | null;
  reference: string | null;
  attestedBy: string;
  attestedByName: string | null;
  attestedAt: string;
  version: number;
};

type QueueState =
  | { kind: "loading" }
  | { kind: "ok"; entries: ConfirmationQueueEntry[] }
  | { kind: "error"; message: string };

// The treasurer's confirmation queue: every pending attestation of a season,
// oldest first, to confirm or reject where it stands.
export function Confirmations() {
  const { isLoading, isAuthenticated, loginWithRedirect } = useAuth0();
  const params = useSearchParams();
  const leagueId = params.get("league");
  const season = params.get("season");

  if (!leagueId || !season) {
    return (
      <p className="text-zinc-700 dark:text-zinc-300">
        No season chosen. Open the confirmation queue from your league.
      </p>
    );
  }

  if (isLoading) {
    return <p className="text-zinc-500">Signing you in…</p>;
  }

  if (!isAuthenticated) {
    return (
      <div className="flex flex-col gap-4">
        <p className="text-zinc-600 dark:text-zinc-400">
          Sign in to work the confirmation queue.
        </p>
        <button
          type="button"
          className="self-start rounded-md bg-emerald-700 px-5 py-2.5 font-medium text-white hover:bg-emerald-800"
          onClick={() => loginWithRedirect()}
        >
          Sign in
        </button>
      </div>
    );
  }

  return (
    <ConfirmationQueue
      key={`${leagueId}/${season}`}
      leagueId={leagueId}
      season={season}
    />
  );
}

function ConfirmationQueue({
  leagueId,
  season,
}: {
  leagueId: string;
  season: string;
}) {
  const { getAccessTokenSilently } = useAuth0();
  const [state, setState] = useState<QueueState>({ kind: "loading" });
  // Bumped to read the queue again: after every decision, since it changes
  // the version of every other attestation on the same account.
  const [reloads, setReloads] = useState(0);
  const [notice, setNotice] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();

    getAccessTokenSilently()
      .then((token) =>
        fetch(
          `${apiBaseUrl}/leagues/${encodeURIComponent(leagueId)}/seasons/${encodeURIComponent(season)}/confirmations`,
          {
            headers: { Authorization: `Bearer ${token}` },
            signal: controller.signal,
          },
        ),
      )
      .then(async (response) => {
        if (response.status === 403) {
          setState({
            kind: "error",
            message: "Only a treasurer can work the confirmation queue.",
          });
          return;
        }
        if (response.status === 404) {
          setState({ kind: "error", message: "There is no such season." });
          return;
        }
        if (!response.ok) {
          setState({ kind: "error", message: await problemMessage(response) });
          return;
        }
        setState({
          kind: "ok",
          entries: (await response.json()) as ConfirmationQueueEntry[],
        });
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return;
        setState({ kind: "error", message: unreachableMessage(error) });
      });

    return () => controller.abort();
  }, [getAccessTokenSilently, leagueId, season, reloads]);

  const header = (
    <header className="flex flex-col gap-1">
      <h1 className="text-3xl font-semibold tracking-tight text-zinc-950 dark:text-zinc-50">
        Confirmation queue
      </h1>
      <p className="text-sm text-zinc-500">
        {season} season ·{" "}
        <Link
          href={`/league?id=${encodeURIComponent(leagueId)}`}
          className="underline decoration-zinc-400 underline-offset-4"
        >
          Back to the league
        </Link>
      </p>
    </header>
  );

  if (state.kind === "loading") {
    return (
      <section className="flex flex-col gap-6">
        {header}
        <p className="text-zinc-500">Loading the queue…</p>
      </section>
    );
  }

  if (state.kind === "error") {
    return (
      <section className="flex flex-col gap-6">
        {header}
        <p className="text-rose-700 dark:text-rose-400">{state.message}</p>
      </section>
    );
  }

  return (
    <section className="flex flex-col gap-6">
      {header}

      {notice && (
        <p
          role="status"
          className="rounded-md bg-amber-50 p-3 text-sm text-amber-900 dark:bg-amber-950 dark:text-amber-200"
        >
          {notice}
        </p>
      )}

      {state.entries.length === 0 ? (
        <p className="text-zinc-600 dark:text-zinc-400">
          Nothing waiting. Every payment attested so far has been confirmed or
          rejected.
        </p>
      ) : (
        <ol className="divide-y divide-zinc-200 rounded-lg border border-zinc-200 bg-white text-sm dark:divide-zinc-800 dark:border-zinc-800 dark:bg-zinc-950">
          {state.entries.map((entry) => (
            <li
              key={entry.attestationId}
              className="flex flex-col gap-2 p-3 sm:flex-row sm:items-start sm:justify-between"
            >
              <div className="flex flex-col gap-0.5">
                <Link
                  href={`/statement?league=${encodeURIComponent(leagueId)}&account=${encodeURIComponent(entry.accountId)}`}
                  className="font-medium text-zinc-950 underline decoration-zinc-400 underline-offset-4 dark:text-zinc-50"
                >
                  {entry.teamName}
                </Link>
                {entry.displayName && (
                  <span className="text-zinc-500">{entry.displayName}</span>
                )}
                <span className="text-zinc-700 dark:text-zinc-300">
                  {formatMoney(entry.amount)} via {entry.rail}
                  {entry.reference && ` · ${entry.reference}`}
                </span>
                <span className="text-xs text-zinc-500">
                  {formatTimestamp(entry.attestedAt)}
                  {entry.attestedByName && ` · by ${entry.attestedByName}`}
                </span>
              </div>
              <SettleAttestation
                leagueId={leagueId}
                accountId={entry.accountId}
                attestationId={entry.attestationId}
                version={entry.version}
                onSettled={() => {
                  setNotice(null);
                  setReloads((count) => count + 1);
                }}
                onVersionConflict={() => {
                  setNotice(
                    `${entry.teamName}'s account changed since the queue was loaded, so nothing was recorded. It has been reloaded: check it and try again.`,
                  );
                  setReloads((count) => count + 1);
                }}
              />
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}
