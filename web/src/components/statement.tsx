"use client";

import { useAuth0 } from "@auth0/auth0-react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useEffect, useState } from "react";
import { apiBaseUrl } from "@/lib/config";
import { describeBalance, formatMoney } from "@/lib/money";
import { problemMessage, unreachableMessage } from "@/lib/problem";

type StatementLine = {
  kind: "Assessment" | "Attestation";
  id: string;
  amount: number;
  by: string;
  byName: string | null;
  at: string;
  // An assessment's.
  memo: string | null;
  dueDate: string | null;
  // An attestation's.
  rail: string | null;
  reference: string | null;
  status: "Pending" | "Confirmed" | "Rejected" | null;
  reason: string | null;
};

type AccountStatement = {
  accountId: string;
  season: string;
  memberId: string;
  teamName: string;
  displayName: string | null;
  // The caller holds this member, rather than reading it as a treasurer.
  yours: boolean;
  balance: number;
  totals: { assessed: number; confirmed: number };
  lines: StatementLine[];
  version: number;
};

type StatementState =
  | { kind: "loading" }
  | { kind: "ok"; statement: AccountStatement }
  | { kind: "error"; message: string };

const statusStyles = {
  Pending:
    "bg-amber-100 text-amber-800 dark:bg-amber-950 dark:text-amber-300",
  Confirmed:
    "bg-emerald-100 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-300",
  Rejected: "bg-rose-100 text-rose-800 dark:bg-rose-950 dark:text-rose-300",
};

function formatDate(iso: string, timeZone?: string): string {
  return new Date(iso).toLocaleDateString(undefined, {
    month: "short",
    day: "numeric",
    year: "numeric",
    timeZone,
  });
}

// A statement needs a signed-in treasurer or the account's own member: the API
// refuses everyone else.
export function Statement() {
  const { isLoading, isAuthenticated, loginWithRedirect } = useAuth0();
  const params = useSearchParams();
  const leagueId = params.get("league");
  const accountId = params.get("account");

  if (!leagueId || !accountId) {
    return (
      <p className="text-zinc-700 dark:text-zinc-300">
        No account chosen. Pick one from your league&apos;s ledger.
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
          Sign in to see this statement.
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
    <StatementView
      key={`${leagueId}/${accountId}`}
      leagueId={leagueId}
      accountId={accountId}
    />
  );
}

function StatementView({
  leagueId,
  accountId,
}: {
  leagueId: string;
  accountId: string;
}) {
  const { getAccessTokenSilently } = useAuth0();
  const [state, setState] = useState<StatementState>({ kind: "loading" });

  useEffect(() => {
    const controller = new AbortController();

    getAccessTokenSilently()
      .then((token) =>
        fetch(
          `${apiBaseUrl}/leagues/${encodeURIComponent(leagueId)}/accounts/${encodeURIComponent(accountId)}`,
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
            message:
              "Only a treasurer, or the account's own member, can see this statement.",
          });
          return;
        }
        if (response.status === 404) {
          setState({ kind: "error", message: "There is no such account." });
          return;
        }
        if (!response.ok) {
          setState({ kind: "error", message: await problemMessage(response) });
          return;
        }
        setState({
          kind: "ok",
          statement: (await response.json()) as AccountStatement,
        });
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return;
        setState({ kind: "error", message: unreachableMessage(error) });
      });

    return () => controller.abort();
  }, [getAccessTokenSilently, leagueId, accountId]);

  if (state.kind === "loading") {
    return <p className="text-zinc-500">Loading the statement…</p>;
  }

  if (state.kind === "error") {
    return <p className="text-rose-700 dark:text-rose-400">{state.message}</p>;
  }

  const { statement } = state;

  return (
    <section className="flex flex-col gap-6">
      <header className="flex flex-col gap-1">
        <h1 className="text-3xl font-semibold tracking-tight text-zinc-950 dark:text-zinc-50">
          {statement.teamName}
        </h1>
        <p className="text-sm text-zinc-500">
          {statement.displayName && `${statement.displayName} · `}
          {statement.season} season ·{" "}
          <Link
            href={`/league?id=${encodeURIComponent(leagueId)}`}
            className="underline decoration-zinc-400 underline-offset-4"
          >
            Back to the league
          </Link>
        </p>
      </header>

      <div className="rounded-lg border border-zinc-200 bg-white p-4 dark:border-zinc-800 dark:bg-zinc-950">
        <p className="text-2xl font-semibold text-zinc-950 dark:text-zinc-50">
          {describeBalance(statement.balance, statement.yours ? "you" : "them")}
        </p>
        <p className="text-sm text-zinc-600 dark:text-zinc-400">
          Assessed {formatMoney(statement.totals.assessed)} · Confirmed paid{" "}
          {formatMoney(statement.totals.confirmed)}
        </p>
      </div>

      <ol className="divide-y divide-zinc-200 rounded-lg border border-zinc-200 bg-white text-sm dark:divide-zinc-800 dark:border-zinc-800 dark:bg-zinc-950">
        {statement.lines.map((line) => (
          <li
            key={`${line.kind}/${line.id}`}
            className="flex flex-col gap-1 p-3 sm:flex-row sm:items-start sm:justify-between"
          >
            <div className="flex flex-col gap-0.5">
              {line.kind === "Assessment" ? (
                <>
                  <span className="font-medium text-zinc-950 dark:text-zinc-50">
                    {line.memo || "Assessment"}
                  </span>
                  {line.dueDate && (
                    <span className="text-zinc-500">
                      Due {formatDate(line.dueDate, "UTC")}
                    </span>
                  )}
                </>
              ) : (
                <>
                  <span className="flex items-center gap-2 font-medium text-zinc-950 dark:text-zinc-50">
                    Paid by {line.rail}
                    {line.reference && ` · ${line.reference}`}
                    {line.status && (
                      <span
                        className={`rounded-full px-2 py-0.5 text-xs font-medium ${statusStyles[line.status]}`}
                      >
                        {line.status}
                      </span>
                    )}
                  </span>
                  {line.status === "Rejected" && line.reason && (
                    <span className="text-rose-700 dark:text-rose-400">
                      {line.reason}
                    </span>
                  )}
                </>
              )}
              <span className="text-xs text-zinc-500">
                {formatDate(line.at)}
                {line.byName && ` · by ${line.byName}`}
              </span>
            </div>
            {/* Only a confirmed payment counts against the balance. */}
            <span
              className={
                line.kind === "Attestation" && line.status !== "Confirmed"
                  ? "text-zinc-400"
                  : "font-medium text-zinc-950 dark:text-zinc-50"
              }
            >
              {line.kind === "Assessment" ? "" : "−"}
              {formatMoney(line.amount)}
            </span>
          </li>
        ))}
      </ol>
    </section>
  );
}
