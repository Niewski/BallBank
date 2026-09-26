"use client";

import { useAuth0 } from "@auth0/auth0-react";
import { useState, type FormEvent } from "react";
import { apiBaseUrl } from "@/lib/config";
import { problemMessage, unreachableMessage } from "@/lib/problem";

type RevokeState =
  | { kind: "idle" }
  | { kind: "revoking" }
  | { kind: "error"; message: string };

// A treasurer revokes whoever holds a member's claim, with a reason: the
// wrong person claimed it, or its owner left. The member keeps its account
// and history, and can be invited again once unclaimed.
export function Revoke({
  leagueId,
  memberId,
  subject,
  teamName,
  onRevoked,
}: {
  leagueId: string;
  memberId: string;
  subject: string;
  teamName: string;
  onRevoked: () => void;
}) {
  const { getAccessTokenSilently } = useAuth0();
  const [confirming, setConfirming] = useState(false);
  const [reason, setReason] = useState("");
  const [state, setState] = useState<RevokeState>({ kind: "idle" });

  async function revoke(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setState({ kind: "revoking" });

    try {
      const token = await getAccessTokenSilently();
      const response = await fetch(
        `${apiBaseUrl}/leagues/${encodeURIComponent(leagueId)}/members/${encodeURIComponent(memberId)}/claims/${encodeURIComponent(subject)}`,
        {
          method: "DELETE",
          headers: {
            Authorization: `Bearer ${token}`,
            "Content-Type": "application/json",
          },
          body: JSON.stringify({ reason }),
        },
      );

      if (!response.ok) {
        setState({ kind: "error", message: await problemMessage(response) });
        return;
      }

      setState({ kind: "idle" });
      setConfirming(false);
      setReason("");
      onRevoked();
    } catch (error: unknown) {
      setState({ kind: "error", message: unreachableMessage(error) });
    }
  }

  if (!confirming) {
    return (
      <button
        type="button"
        onClick={() => setConfirming(true)}
        aria-label={`Revoke ${teamName}'s claim`}
        className="text-xs font-medium text-rose-700 hover:underline dark:text-rose-400"
      >
        Revoke
      </button>
    );
  }

  const revoking = state.kind === "revoking";
  const field =
    "min-w-0 rounded-md border border-zinc-300 bg-white px-2 py-1 text-zinc-950 dark:border-zinc-700 dark:bg-zinc-950 dark:text-zinc-50";

  return (
    <form
      onSubmit={revoke}
      aria-label={`Revoke ${teamName}'s claim`}
      className="flex min-w-56 flex-col gap-2"
    >
      <label className="flex flex-col gap-0.5 text-xs text-zinc-500">
        Reason
        <input
          name="reason"
          required
          autoComplete="off"
          value={reason}
          onChange={(event) => setReason(event.target.value)}
          className={field}
        />
      </label>
      <div className="flex gap-2">
        <button
          type="submit"
          disabled={revoking}
          className="rounded-md bg-rose-700 px-3 py-1 text-xs font-medium text-white hover:bg-rose-800 disabled:opacity-50"
        >
          {revoking ? "Revoking…" : "Revoke"}
        </button>
        <button
          type="button"
          disabled={revoking}
          onClick={() => {
            setConfirming(false);
            setState({ kind: "idle" });
          }}
          className="rounded-md px-3 py-1 text-xs font-medium text-zinc-700 hover:bg-zinc-100 disabled:opacity-50 dark:text-zinc-300 dark:hover:bg-zinc-900"
        >
          Cancel
        </button>
      </div>
      <div aria-live="polite" className="text-xs">
        {state.kind === "error" && (
          <p className="text-rose-700 dark:text-rose-400">{state.message}</p>
        )}
      </div>
    </form>
  );
}
