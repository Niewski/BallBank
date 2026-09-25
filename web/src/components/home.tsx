"use client";

import { useAuth0 } from "@auth0/auth0-react";
import { ApiStatus } from "@/components/api-status";
import { MyLeagues } from "@/components/my-leagues";

// Signed out: what BallBank is and a way in. Signed in: "My leagues".
export function Home() {
  const { isLoading, isAuthenticated, user, error, loginWithRedirect, logout } =
    useAuth0();

  if (isLoading) {
    return <p className="text-zinc-500">Signing you in…</p>;
  }

  if (isAuthenticated) {
    return (
      <div className="flex flex-col gap-8">
        <div className="flex items-center justify-between gap-4 text-sm text-zinc-600 dark:text-zinc-400">
          <span>Signed in as {user?.email ?? user?.name}</span>
          <button
            type="button"
            className="underline decoration-zinc-400 underline-offset-4 hover:text-zinc-900 dark:hover:text-zinc-100"
            onClick={() =>
              logout({ logoutParams: { returnTo: window.location.origin } })
            }
          >
            Sign out
          </button>
        </div>
        <MyLeagues />
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-6">
      {error && (
        <p className="text-rose-700 dark:text-rose-400">
          Sign-in failed: {error.message}
        </p>
      )}
      <button
        type="button"
        className="self-start rounded-md bg-emerald-700 px-5 py-2.5 font-medium text-white hover:bg-emerald-800"
        onClick={() => loginWithRedirect()}
      >
        Sign in
      </button>
      <ApiStatus />
    </div>
  );
}
