"use client";

import { useAuth0 } from "@auth0/auth0-react";
import Link from "next/link";
import { useEffect, useState } from "react";
import { apiBaseUrl } from "@/lib/config";
import { describeBalance } from "@/lib/money";
import { problemMessage, unreachableMessage } from "@/lib/problem";

type LedgerEntry = {
  accountId: string;
  memberId: string;
  teamName: string;
  displayName: string | null;
  balance: number;
  pendingAttestations: number;
  version: number;
};

type LedgerState =
  | { kind: "loading" }
  | { kind: "ok"; accounts: LedgerEntry[] }
  | { kind: "error"; message: string };

// Every account of the open season, for every member. A treasurer can open
// any account's statement; a member only their own.
export function Ledger({
  leagueId,
  season,
  yourMemberId,
  youAreTreasurer,
}: {
  leagueId: string;
  season: string;
  yourMemberId: string | null;
  youAreTreasurer: boolean;
}) {
  const { getAccessTokenSilently } = useAuth0();
  const [state, setState] = useState<LedgerState>({ kind: "loading" });

  useEffect(() => {
    const controller = new AbortController();

    getAccessTokenSilently()
      .then((token) =>
        fetch(
          `${apiBaseUrl}/leagues/${encodeURIComponent(leagueId)}/seasons/${encodeURIComponent(season)}/ledger`,
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
          accounts: (await response.json()) as LedgerEntry[],
        });
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return;
        setState({ kind: "error", message: unreachableMessage(error) });
      });

    return () => controller.abort();
  }, [getAccessTokenSilently, leagueId, season]);

  if (state.kind === "loading") {
    return <p className="text-sm text-zinc-500">Loading the ledger…</p>;
  }

  if (state.kind === "error") {
    return (
      <p className="text-sm text-rose-700 dark:text-rose-400">
        {state.message}
      </p>
    );
  }

  return (
    <div className="overflow-x-auto rounded-lg border border-zinc-200 bg-white dark:border-zinc-800 dark:bg-zinc-950">
      <table className="w-full text-left text-sm">
        <caption className="p-3 text-left font-medium text-zinc-950 dark:text-zinc-50">
          Ledger
        </caption>
        <thead className="border-b border-zinc-200 text-zinc-500 dark:border-zinc-800">
          <tr>
            <th scope="col" className="p-3 font-medium">
              Team
            </th>
            <th scope="col" className="p-3 font-medium">
              Balance
            </th>
            <th scope="col" className="p-3 font-medium">
              Pending
            </th>
          </tr>
        </thead>
        <tbody className="divide-y divide-zinc-200 dark:divide-zinc-800">
          {state.accounts.map((account) => {
            const yours = account.memberId === yourMemberId;
            return (
              <tr key={account.accountId}>
                <th
                  scope="row"
                  className="p-3 font-medium text-zinc-950 dark:text-zinc-50"
                >
                  {youAreTreasurer || yours ? (
                    <Link
                      href={`/statement?league=${encodeURIComponent(leagueId)}&account=${encodeURIComponent(account.accountId)}`}
                      className="underline decoration-zinc-400 underline-offset-4"
                    >
                      {account.teamName}
                    </Link>
                  ) : (
                    account.teamName
                  )}
                  {account.displayName && (
                    <span className="ml-2 font-normal text-zinc-500">
                      {account.displayName}
                    </span>
                  )}
                </th>
                <td className="p-3 text-zinc-700 dark:text-zinc-300">
                  {describeBalance(account.balance, yours ? "you" : "them")}
                </td>
                <td className="p-3 text-zinc-700 dark:text-zinc-300">
                  {account.pendingAttestations > 0 ? (
                    account.pendingAttestations
                  ) : (
                    <span className="text-zinc-400">None</span>
                  )}
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
