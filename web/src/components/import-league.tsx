"use client";

import { useAuth0 } from "@auth0/auth0-react";
import { SleeperLeaguePicker } from "@/components/sleeper-league-picker";

// Importing needs a signed-in person: the API only answers bearer tokens.
export function ImportLeague() {
  const { isLoading, isAuthenticated, loginWithRedirect } = useAuth0();

  if (isLoading) {
    return <p className="text-zinc-500">Signing you in…</p>;
  }

  if (!isAuthenticated) {
    return (
      <div className="flex flex-col gap-4">
        <p className="text-zinc-600 dark:text-zinc-400">
          Sign in to import a league.
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

  return <SleeperLeaguePicker />;
}
