"use client";

import { useAuth0 } from "@auth0/auth0-react";
import { useSearchParams } from "next/navigation";
import { MemberList } from "@/components/member-list";

// A league's page needs a signed-in member: the API refuses everyone else.
export function League() {
  const { isLoading, isAuthenticated, loginWithRedirect } = useAuth0();
  const leagueId = useSearchParams().get("id");

  if (!leagueId) {
    return (
      <p className="text-zinc-700 dark:text-zinc-300">
        No league chosen. Pick one from My leagues.
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
          Sign in to see this league.
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

  return <MemberList key={leagueId} leagueId={leagueId} />;
}
