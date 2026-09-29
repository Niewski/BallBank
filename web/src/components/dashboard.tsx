"use client";

import { useAuth0 } from "@auth0/auth0-react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useEffect, useState } from "react";
import { DiscordSettings } from "@/components/discord-settings";
import { apiBaseUrl } from "@/lib/config";
import { formatAgo, formatDueDate } from "@/lib/dates";
import { formatMoney } from "@/lib/money";
import { problemMessage, unreachableMessage } from "@/lib/problem";

type DashboardFigures = {
  assessed: number;
  confirmed: number;
  refunded: number;
  adjusted: number;
  pot: number;
  outstanding: number;
  owed: number;
  pendingAttestations: number;
};

type Delinquent = {
  accountId: string;
  memberId: string;
  teamName: string;
  displayName: string | null;
  balance: number;
  earliestDueDate: string;
  daysOverdue: number;
};

type SeasonDashboard = {
  season: string;
  figures: DashboardFigures;
  delinquents: Delinquent[];
  asOf: string | null;
};

type DashboardState =
  | { kind: "loading" }
  | { kind: "ok"; dashboard: SeasonDashboard }
  | { kind: "error"; message: string };

// The treasurer's dashboard for a season: what the accounts add up to and who
// is delinquent. It is built a moment after the events, so it says how current it is.
export function Dashboard() {
  const { isLoading, isAuthenticated, loginWithRedirect } = useAuth0();
  const params = useSearchParams();
  const leagueId = params.get("league");
  const season = params.get("season");

  if (!leagueId || !season) {
    return (
      <p className="text-zinc-700 dark:text-zinc-300">
        No season chosen. Open the dashboard from your league.
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
          Sign in to see the dashboard.
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
    <SeasonDashboardView
      key={`${leagueId}/${season}`}
      leagueId={leagueId}
      season={season}
    />
  );
}

function SeasonDashboardView({
  leagueId,
  season,
}: {
  leagueId: string;
  season: string;
}) {
  const { getAccessTokenSilently } = useAuth0();
  const [state, setState] = useState<DashboardState>({ kind: "loading" });
  // Bumped to read the dashboard again: on request, and after any command the
  // treasurer issues from this page.
  const [reloads, setReloads] = useState(0);
  // What "a moment ago" is measured against; moves on while the page is open.
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    const controller = new AbortController();

    getAccessTokenSilently()
      .then((token) =>
        fetch(
          `${apiBaseUrl}/leagues/${encodeURIComponent(leagueId)}/seasons/${encodeURIComponent(season)}/dashboard`,
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
            message: "Only a treasurer can see the dashboard.",
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
        setNow(Date.now());
        setState({
          kind: "ok",
          dashboard: (await response.json()) as SeasonDashboard,
        });
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return;
        setState({ kind: "error", message: unreachableMessage(error) });
      });

    return () => controller.abort();
  }, [getAccessTokenSilently, leagueId, season, reloads]);

  useEffect(() => {
    const tick = setInterval(() => setNow(Date.now()), 30_000);
    return () => clearInterval(tick);
  }, []);

  const header = (
    <header className="flex flex-col gap-1">
      <h1 className="text-3xl font-semibold tracking-tight text-zinc-950 dark:text-zinc-50">
        Dashboard
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
        <p className="text-zinc-500">Loading the dashboard…</p>
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

  const { figures, delinquents, asOf } = state.dashboard;

  return (
    <section className="flex flex-col gap-6">
      {header}

      <div className="flex items-center justify-between gap-4 text-sm text-zinc-500">
        <p role="status">
          {asOf
            ? `Updated ${formatAgo(asOf, now)}`
            : "The dashboard has not been built yet. Refresh in a moment."}
        </p>
        <button
          type="button"
          className="underline decoration-zinc-400 underline-offset-4"
          onClick={() => setReloads((count) => count + 1)}
        >
          Refresh
        </button>
      </div>

      <dl className="grid grid-cols-2 gap-3 text-sm sm:grid-cols-3">
        <Figure label="In the pot" value={formatMoney(figures.pot)} />
        <Figure label="Outstanding" value={formatMoney(figures.outstanding)} />
        <Figure label="Pot owes members" value={formatMoney(figures.owed)} />
        <Figure label="Assessed" value={formatMoney(figures.assessed)} />
        <Figure label="Confirmed paid" value={formatMoney(figures.confirmed)} />
        <Figure label="Refunded" value={formatMoney(figures.refunded)} />
      </dl>

      {figures.pendingAttestations > 0 && (
        <p className="text-sm text-zinc-600 dark:text-zinc-400">
          {figures.pendingAttestations} payment
          {figures.pendingAttestations === 1 ? " is" : "s are"} waiting for a
          treasurer.{" "}
          <Link
            href={`/confirmations?league=${encodeURIComponent(leagueId)}&season=${encodeURIComponent(season)}`}
            className="underline decoration-zinc-400 underline-offset-4"
          >
            Confirmation queue
          </Link>
        </p>
      )}

      {delinquents.length === 0 ? (
        <p className="text-zinc-600 dark:text-zinc-400">
          Nobody is behind on their dues.
        </p>
      ) : (
        <div className="overflow-x-auto rounded-lg border border-zinc-200 bg-white dark:border-zinc-800 dark:bg-zinc-950">
          <table className="w-full text-left text-sm">
            <caption className="p-3 text-left font-medium text-zinc-950 dark:text-zinc-50">
              Delinquent
            </caption>
            <thead className="border-b border-zinc-200 text-zinc-500 dark:border-zinc-800">
              <tr>
                <th scope="col" className="p-3 font-medium">
                  Team
                </th>
                <th scope="col" className="p-3 font-medium">
                  Owes
                </th>
                <th scope="col" className="p-3 font-medium">
                  Days overdue
                </th>
              </tr>
            </thead>
            <tbody className="divide-y divide-zinc-200 dark:divide-zinc-800">
              {delinquents.map((delinquent) => (
                <tr key={delinquent.accountId}>
                  <th
                    scope="row"
                    className="p-3 font-medium text-zinc-950 dark:text-zinc-50"
                  >
                    <Link
                      href={`/statement?league=${encodeURIComponent(leagueId)}&account=${encodeURIComponent(delinquent.accountId)}`}
                      className="underline decoration-zinc-400 underline-offset-4"
                    >
                      {delinquent.teamName}
                    </Link>
                    {delinquent.displayName && (
                      <span className="ml-2 font-normal text-zinc-500">
                        {delinquent.displayName}
                      </span>
                    )}
                  </th>
                  <td className="p-3 text-zinc-700 dark:text-zinc-300">
                    {formatMoney(delinquent.balance)}
                  </td>
                  <td className="p-3 text-zinc-700 dark:text-zinc-300">
                    {delinquent.daysOverdue}
                    <span className="ml-2 text-xs text-zinc-500">
                      due {formatDueDate(delinquent.earliestDueDate)}
                    </span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <DiscordSettings leagueId={leagueId} />
    </section>
  );
}

function Figure({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border border-zinc-200 bg-white p-3 dark:border-zinc-800 dark:bg-zinc-950">
      <dt className="text-zinc-500">{label}</dt>
      <dd className="text-xl font-semibold text-zinc-950 dark:text-zinc-50">
        {value}
      </dd>
    </div>
  );
}
