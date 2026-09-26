"use client";

import { useAuth0 } from "@auth0/auth0-react";
import { useState, type ChangeEvent, type FormEvent } from "react";
import { apiBaseUrl } from "@/lib/config";
import { idempotencyHeader, useIdempotencyKey } from "@/lib/idempotency";
import {
  problemMessage,
  unreachableMessage,
  versionConflictType,
} from "@/lib/problem";
import type { AccountStatement } from "./statement";

const rails = [
  "Cash",
  "Venmo",
  "Zelle",
  "PayPal",
  "CashApp",
  "Check",
  "Other",
] as const;

type Rail = (typeof rails)[number];

type AttestState =
  | { kind: "idle" }
  | { kind: "attesting" }
  | { kind: "error"; message: string };

// "I paid": the account's member, or a treasurer they handed cash to, says a
// payment was made. It shows as pending and the balance does not move until a
// treasurer confirms it. The amount starts at the balance but any amount goes:
// part of it, or more. Every rail but Cash needs a reference so the treasurer
// can find the payment.
//
// The request carries the account's version as last read (ADR-0005). If
// someone changed the account since, the API refuses it and the statement is
// reloaded so the person can check it and try again.
export function AttestPayment({
  leagueId,
  accountId,
  balance,
  version,
  onAttested,
  onVersionConflict,
}: {
  leagueId: string;
  accountId: string;
  balance: number;
  version: number;
  onAttested: (statement: AccountStatement) => void;
  onVersionConflict: () => void;
}) {
  const { getAccessTokenSilently } = useAuth0();
  const [amount, setAmount] = useState(balance > 0 ? balance.toFixed(2) : "");
  const [rail, setRail] = useState<Rail>("Venmo");
  const [reference, setReference] = useState("");
  const [state, setState] = useState<AttestState>({ kind: "idle" });
  // One key, and one attestation id, per submission: kept while it is
  // retried, fresh once it has gone through or the form has changed.
  const idempotencyKey = useIdempotencyKey();
  const attestationId = useIdempotencyKey();

  function changed() {
    idempotencyKey.reset();
    attestationId.reset();
  }

  function edit(set: (value: string) => void) {
    return (event: ChangeEvent<HTMLInputElement>) => {
      changed();
      set(event.target.value);
    };
  }

  async function attest(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setState({ kind: "attesting" });

    try {
      const token = await getAccessTokenSilently();
      const response = await fetch(
        `${apiBaseUrl}/leagues/${encodeURIComponent(leagueId)}/accounts/${encodeURIComponent(accountId)}/attestations`,
        {
          method: "POST",
          headers: {
            Authorization: `Bearer ${token}`,
            "Content-Type": "application/json",
            [idempotencyHeader]: idempotencyKey.current(),
          },
          body: JSON.stringify({
            attestationId: attestationId.current(),
            amount: Number(amount),
            rail,
            reference: rail === "Cash" ? null : reference,
            version,
          }),
        },
      );

      if (response.status === 409) {
        const problem = (await response.clone().json().catch(() => null)) as {
          type?: string;
        } | null;
        if (problem?.type === versionConflictType) {
          changed();
          setState({ kind: "idle" });
          onVersionConflict();
          return;
        }
      }

      if (!response.ok) {
        setState({ kind: "error", message: await problemMessage(response) });
        return;
      }

      changed();
      setState({ kind: "idle" });
      onAttested((await response.json()) as AccountStatement);
    } catch (error: unknown) {
      setState({ kind: "error", message: unreachableMessage(error) });
    }
  }

  const attesting = state.kind === "attesting";
  const field =
    "min-w-0 rounded-md border border-zinc-300 bg-white px-2 py-1 text-zinc-950 dark:border-zinc-700 dark:bg-zinc-950 dark:text-zinc-50";

  return (
    <form
      onSubmit={attest}
      aria-label="I paid"
      className="flex flex-col gap-3 rounded-lg border border-zinc-200 bg-white p-4 dark:border-zinc-800 dark:bg-zinc-950"
    >
      <p className="text-sm font-medium text-zinc-950 dark:text-zinc-50">
        I paid
      </p>
      <div className="flex flex-wrap gap-3">
        <label className="flex flex-col gap-0.5 text-xs text-zinc-500">
          Amount
          <input
            name="amount"
            type="number"
            min="0.01"
            step="0.01"
            required
            value={amount}
            onChange={edit(setAmount)}
            className={field}
          />
        </label>
        <label className="flex flex-col gap-0.5 text-xs text-zinc-500">
          Paid with
          <select
            name="rail"
            value={rail}
            onChange={(event) => {
              changed();
              setRail(event.target.value as Rail);
            }}
            className={field}
          >
            {rails.map((option) => (
              <option key={option} value={option}>
                {option}
              </option>
            ))}
          </select>
        </label>
        {rail !== "Cash" && (
          <label className="flex flex-col gap-0.5 text-xs text-zinc-500">
            Reference
            <input
              name="reference"
              required
              autoComplete="off"
              placeholder="So the treasurer can find it"
              value={reference}
              onChange={edit(setReference)}
              className={field}
            />
          </label>
        )}
      </div>
      <button
        type="submit"
        disabled={attesting}
        className="self-start rounded-md bg-emerald-700 px-3 py-1.5 text-xs font-medium text-white hover:bg-emerald-800 disabled:opacity-50"
      >
        {attesting ? "Sending…" : "I paid"}
      </button>
      <p className="text-xs text-zinc-500">
        It shows as pending until a treasurer confirms it.
      </p>
      <div aria-live="polite" className="text-xs">
        {state.kind === "error" && (
          <p className="text-rose-700 dark:text-rose-400">{state.message}</p>
        )}
      </div>
    </form>
  );
}
