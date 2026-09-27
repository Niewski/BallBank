"use client";

import { useAuth0 } from "@auth0/auth0-react";
import { useState, type FormEvent } from "react";
import { apiBaseUrl } from "@/lib/config";
import { idempotencyHeader, useIdempotencyKey } from "@/lib/idempotency";
import {
  problemMessage,
  unreachableMessage,
  versionConflictType,
} from "@/lib/problem";
import type { AccountStatement } from "./statement";

type SettleState =
  | { kind: "idle" }
  | { kind: "sending" }
  | { kind: "error"; message: string };

// A treasurer's confirm and reject for one pending attestation. Confirming is
// one click and moves the balance. Rejecting asks for a reason, which the
// member sees on their statement; they can then attest again.
//
// Each request carries the account's version as last read (ADR-0005). If
// someone changed the account since, nothing is recorded and the caller
// reloads so the treasurer can check it and try again.
export function SettleAttestation({
  leagueId,
  accountId,
  attestationId,
  version,
  onSettled,
  onVersionConflict,
}: {
  leagueId: string;
  accountId: string;
  attestationId: string;
  version: number;
  onSettled: (statement: AccountStatement) => void;
  onVersionConflict: () => void;
}) {
  const { getAccessTokenSilently } = useAuth0();
  const [state, setState] = useState<SettleState>({ kind: "idle" });
  // Asking for a reason, rather than showing confirm and reject.
  const [rejecting, setRejecting] = useState(false);
  const [reason, setReason] = useState("");
  // One key per decision: kept while it is retried, fresh once it has gone
  // through or the reason has changed.
  const idempotencyKey = useIdempotencyKey();

  async function send(decision: "confirmation" | "rejection", body: object) {
    setState({ kind: "sending" });

    try {
      const token = await getAccessTokenSilently();
      const response = await fetch(
        `${apiBaseUrl}/leagues/${encodeURIComponent(leagueId)}/accounts/${encodeURIComponent(accountId)}/attestations/${encodeURIComponent(attestationId)}/${decision}`,
        {
          method: "POST",
          headers: {
            Authorization: `Bearer ${token}`,
            "Content-Type": "application/json",
            [idempotencyHeader]: idempotencyKey.current(),
          },
          body: JSON.stringify({ ...body, version }),
        },
      );

      if (response.status === 409) {
        const problem = (await response.clone().json().catch(() => null)) as {
          type?: string;
        } | null;
        if (problem?.type === versionConflictType) {
          idempotencyKey.reset();
          setState({ kind: "idle" });
          onVersionConflict();
          return;
        }
      }

      if (!response.ok) {
        setState({ kind: "error", message: await problemMessage(response) });
        return;
      }

      idempotencyKey.reset();
      setState({ kind: "idle" });
      onSettled((await response.json()) as AccountStatement);
    } catch (error: unknown) {
      setState({ kind: "error", message: unreachableMessage(error) });
    }
  }

  function reject(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    void send("rejection", { reason });
  }

  const sending = state.kind === "sending";
  const button =
    "rounded-md px-3 py-1.5 text-xs font-medium disabled:opacity-50";

  return (
    <div className="flex flex-col gap-2">
      {rejecting ? (
        <form
          onSubmit={reject}
          aria-label="Reject payment"
          className="flex flex-wrap items-end gap-2"
        >
          <label className="flex flex-col gap-0.5 text-xs text-zinc-500">
            Why? The member will see this.
            <input
              name="reason"
              required
              autoFocus
              autoComplete="off"
              value={reason}
              onChange={(event) => {
                idempotencyKey.reset();
                setReason(event.target.value);
              }}
              className="min-w-0 rounded-md border border-zinc-300 bg-white px-2 py-1 text-zinc-950 dark:border-zinc-700 dark:bg-zinc-950 dark:text-zinc-50"
            />
          </label>
          <button
            type="submit"
            disabled={sending}
            className={`${button} bg-rose-700 text-white hover:bg-rose-800`}
          >
            {sending ? "Rejecting…" : "Reject"}
          </button>
          <button
            type="button"
            disabled={sending}
            onClick={() => {
              idempotencyKey.reset();
              setReason("");
              setRejecting(false);
              setState({ kind: "idle" });
            }}
            className={`${button} text-zinc-600 hover:text-zinc-950 dark:text-zinc-400 dark:hover:text-zinc-50`}
          >
            Cancel
          </button>
        </form>
      ) : (
        <div className="flex gap-2">
          <button
            type="button"
            disabled={sending}
            onClick={() => void send("confirmation", {})}
            className={`${button} bg-emerald-700 text-white hover:bg-emerald-800`}
          >
            {sending ? "Confirming…" : "Confirm"}
          </button>
          <button
            type="button"
            disabled={sending}
            onClick={() => setRejecting(true)}
            className={`${button} border border-zinc-300 text-zinc-700 hover:bg-zinc-100 dark:border-zinc-700 dark:text-zinc-300 dark:hover:bg-zinc-900`}
          >
            Reject
          </button>
        </div>
      )}
      <div aria-live="polite" className="text-xs">
        {state.kind === "error" && (
          <p className="text-rose-700 dark:text-rose-400">{state.message}</p>
        )}
      </div>
    </div>
  );
}
