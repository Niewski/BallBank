"use client";

import { useAuth0 } from "@auth0/auth0-react";
import { useEffect, useState } from "react";
import { ContactDetails, type Contact } from "@/components/contact-details";
import { ImportAgain } from "@/components/import-again";
import { apiBaseUrl } from "@/lib/config";
import { problemMessage, unreachableMessage } from "@/lib/problem";

type Member = {
  memberId: string;
  teamName: string;
  sleeperDisplayName: string | null;
  claimed: boolean;
  holderDisplayName: string | null;
  suggestedTreasurer: boolean;
  roles: string[];
  // Only for a treasurer, and for the member themselves; null for anyone else.
  contact: Contact | null;
};

type LeagueMembers = {
  leagueId: string;
  name: string;
  season: string;
  yourMemberId: string | null;
  members: Member[];
};

type MembersState =
  | { kind: "loading" }
  | { kind: "ok"; league: LeagueMembers }
  | { kind: "error"; message: string };

export function MemberList({ leagueId }: { leagueId: string }) {
  const { getAccessTokenSilently } = useAuth0();
  const [state, setState] = useState<MembersState>({ kind: "loading" });
  // Bumped to read the members again, as after importing the league again.
  const [reads, setReads] = useState(0);

  useEffect(() => {
    const controller = new AbortController();

    getAccessTokenSilently()
      .then((token) =>
        fetch(`${apiBaseUrl}/leagues/${encodeURIComponent(leagueId)}/members`, {
          headers: { Authorization: `Bearer ${token}` },
          signal: controller.signal,
        }),
      )
      .then(async (response) => {
        if (response.status === 403) {
          setState({
            kind: "error",
            message:
              "You are not a member of this league. Ask its treasurer for an invite.",
          });
          return;
        }
        if (!response.ok) {
          setState({ kind: "error", message: await problemMessage(response) });
          return;
        }
        setState({
          kind: "ok",
          league: (await response.json()) as LeagueMembers,
        });
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return;
        setState({ kind: "error", message: unreachableMessage(error) });
      });

    return () => controller.abort();
  }, [getAccessTokenSilently, leagueId, reads]);

  if (state.kind === "loading") {
    return <p className="text-zinc-500">Loading the league…</p>;
  }

  if (state.kind === "error") {
    return <p className="text-rose-700 dark:text-rose-400">{state.message}</p>;
  }

  const { league } = state;
  const you = league.members.find((m) => m.memberId === league.yourMemberId);
  const showsContacts = league.members.some((m) => m.contact !== null);

  return (
    <section className="flex flex-col gap-6">
      <header className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
        <div className="flex flex-col gap-1">
          <h1 className="text-3xl font-semibold tracking-tight text-zinc-950 dark:text-zinc-50">
            {league.name}
          </h1>
          <p className="text-sm text-zinc-500">
            {league.season} season · {league.members.length} members
          </p>
        </div>
        {you?.roles.includes("Treasurer") && (
          <ImportAgain
            leagueId={league.leagueId}
            onImported={() => setReads((n) => n + 1)}
          />
        )}
      </header>

      <div className="overflow-x-auto rounded-lg border border-zinc-200 bg-white dark:border-zinc-800 dark:bg-zinc-950">
        <table className="w-full text-left text-sm">
          <thead className="border-b border-zinc-200 text-zinc-500 dark:border-zinc-800">
            <tr>
              <th scope="col" className="p-3 font-medium">
                Team
              </th>
              <th scope="col" className="p-3 font-medium">
                On Sleeper
              </th>
              <th scope="col" className="p-3 font-medium">
                Held by
              </th>
              <th scope="col" className="p-3 font-medium">
                Roles
              </th>
              {showsContacts && (
                <th scope="col" className="p-3 font-medium">
                  Contact
                </th>
              )}
            </tr>
          </thead>
          <tbody className="divide-y divide-zinc-200 dark:divide-zinc-800">
            {league.members.map((member) => (
              <tr key={member.memberId}>
                <th
                  scope="row"
                  className="p-3 font-medium text-zinc-950 dark:text-zinc-50"
                >
                  {member.teamName}
                </th>
                <td className="p-3 text-zinc-700 dark:text-zinc-300">
                  {member.sleeperDisplayName ?? (
                    <span className="text-zinc-400">Nobody on Sleeper</span>
                  )}
                </td>
                <td className="p-3">
                  {member.claimed ? (
                    <span className="text-zinc-700 dark:text-zinc-300">
                      {member.holderDisplayName}
                    </span>
                  ) : (
                    <span className="text-zinc-400">Unclaimed</span>
                  )}
                </td>
                <td className="p-3">
                  <div className="flex flex-wrap gap-2">
                    {member.roles.map((role) => (
                      <span
                        key={role}
                        className="rounded-full bg-emerald-100 px-2 py-0.5 text-xs font-medium text-emerald-800 dark:bg-emerald-950 dark:text-emerald-300"
                      >
                        {role}
                      </span>
                    ))}
                    {member.suggestedTreasurer && (
                      <span
                        className="rounded-full bg-zinc-100 px-2 py-0.5 text-xs font-medium text-zinc-600 dark:bg-zinc-900 dark:text-zinc-400"
                        title="Sleeper marks this team's owner as a commissioner"
                      >
                        Suggested treasurer
                      </span>
                    )}
                  </div>
                </td>
                {showsContacts && (
                  <td className="p-3 align-top">
                    {member.contact && (
                      <ContactDetails
                        leagueId={league.leagueId}
                        memberId={member.memberId}
                        teamName={member.teamName}
                        claimed={member.claimed}
                        contact={member.contact}
                        onSaved={() => setReads((n) => n + 1)}
                      />
                    )}
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}
