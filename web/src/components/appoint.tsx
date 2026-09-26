"use client";

import { useAuth0 } from "@auth0/auth0-react";
import { useState } from "react";
import { apiBaseUrl } from "@/lib/config";
import { problemMessage, unreachableMessage } from "@/lib/problem";

type AppointState =
  | { kind: "idle" }
  | { kind: "appointing" }
  | { kind: "error"; message: string };

// A treasurer makes a claimed member another treasurer in one click. There is
// one Treasurer role, so appointing someone twice changes nothing.
export function Appoint({
  leagueId,
  memberId,
  teamName,
  onAppointed,
}: {
  leagueId: string;
  memberId: string;
  teamName: string;
  onAppointed: () => void;
}) {
  const { getAccessTokenSilently } = useAuth0();
  const [state, setState] = useState<AppointState>({ kind: "idle" });

  async function appoint() {
    setState({ kind: "appointing" });

    try {
      const token = await getAccessTokenSilently();
      const response = await fetch(
        `${apiBaseUrl}/leagues/${encodeURIComponent(leagueId)}/treasurers`,
        {
          method: "POST",
          headers: {
            Authorization: `Bearer ${token}`,
            "Content-Type": "application/json",
          },
          body: JSON.stringify({ memberId }),
        },
      );

      if (!response.ok) {
        setState({ kind: "error", message: await problemMessage(response) });
        return;
      }

      setState({ kind: "idle" });
      onAppointed();
    } catch (error: unknown) {
      setState({ kind: "error", message: unreachableMessage(error) });
    }
  }

  return (
    <div className="flex flex-col items-start gap-1">
      <button
        type="button"
        onClick={appoint}
        disabled={state.kind === "appointing"}
        aria-label={`Appoint ${teamName} as a treasurer`}
        className="text-xs font-medium text-emerald-700 hover:underline disabled:opacity-50 dark:text-emerald-400"
      >
        {state.kind === "appointing" ? "Appointing…" : "Appoint"}
      </button>
      <div aria-live="polite" className="text-xs">
        {state.kind === "error" && (
          <p className="text-rose-700 dark:text-rose-400">{state.message}</p>
        )}
      </div>
    </div>
  );
}
