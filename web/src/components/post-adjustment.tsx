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

type Direction = "lower" | "raise";

type AdjustState =
  | { kind: "idle" }
  | { kind: "posting" }
  | { kind: "error"; message: string };

// A treasurer corrects the balance, and says why: a waiver lowers what the
// member owes, a refund of an overpayment raises it back to settled, a
// correction goes either way. The member reads the reason on their statement.
// Only treasurers see this form; the API refuses anyone else.
//
// The request carries the account's version as last read (ADR-0005). If
// someone changed the account since, the API refuses it and the statement is
// reloaded so the treasurer can check it and try again.
export function PostAdjustment({
  leagueId,
  accountId,
  version,
  onPosted,
  onVersionConflict,
}: {
  leagueId: string;
  accountId: string;
  version: number;
  onPosted: (statement: AccountStatement) => void;
  onVersionConflict: () => void;
}) {
  const { getAccessTokenSilently } = useAuth0();
  const [direction, setDirection] = useState<Direction>("lower");
  const [amount, setAmount] = useState("");
  const [reason, setReason] = useState("");
  const [state, setState] = useState<AdjustState>({ kind: "idle" });
  // One key, and one adjustment id, per submission: kept while it is retried,
  // fresh once it has gone through or the form has changed.
  const idempotencyKey = useIdempotencyKey();
  const adjustmentId = useIdempotencyKey();

  function changed() {
    idempotencyKey.reset();
    adjustmentId.reset();
  }

  function edit(set: (value: string) => void) {
    return (event: ChangeEvent<HTMLInputElement>) => {
      changed();
      set(event.target.value);
    };
  }

  async function post(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setState({ kind: "posting" });

    try {
      const token = await getAccessTokenSilently();
      const response = await fetch(
        `${apiBaseUrl}/leagues/${encodeURIComponent(leagueId)}/accounts/${encodeURIComponent(accountId)}/adjustments`,
        {
          method: "POST",
          headers: {
            Authorization: `Bearer ${token}`,
            "Content-Type": "application/json",
            [idempotencyHeader]: idempotencyKey.current(),
          },
          body: JSON.stringify({
            adjustmentId: adjustmentId.current(),
            amount: direction === "lower" ? -Number(amount) : Number(amount),
            reason,
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
      onPosted((await response.json()) as AccountStatement);
    } catch (error: unknown) {
      setState({ kind: "error", message: unreachableMessage(error) });
    }
  }

  const posting = state.kind === "posting";
  const field =
    "min-w-0 rounded-md border border-zinc-300 bg-white px-2 py-1 text-zinc-950 dark:border-zinc-700 dark:bg-zinc-950 dark:text-zinc-50";

  return (
    <form
      onSubmit={post}
      aria-label="Post adjustment"
      className="flex flex-col gap-3 rounded-lg border border-zinc-200 bg-white p-4 dark:border-zinc-800 dark:bg-zinc-950"
    >
      <p className="text-sm font-medium text-zinc-950 dark:text-zinc-50">
        Post adjustment
      </p>
      <div className="flex flex-wrap gap-3">
        <label className="flex flex-col gap-0.5 text-xs text-zinc-500">
          Direction
          <select
            name="direction"
            value={direction}
            onChange={(event) => {
              changed();
              setDirection(event.target.value as Direction);
            }}
            className={field}
          >
            <option value="lower">Lower what they owe (a waiver)</option>
            <option value="raise">Raise what they owe (a refund paid back)</option>
          </select>
        </label>
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
          Reason
          <input
            name="reason"
            required
            autoComplete="off"
            placeholder="The member will see this"
            value={reason}
            onChange={edit(setReason)}
            className={field}
          />
        </label>
      </div>
      <button
        type="submit"
        disabled={posting}
        className="self-start rounded-md bg-emerald-700 px-3 py-1.5 text-xs font-medium text-white hover:bg-emerald-800 disabled:opacity-50"
      >
        {posting ? "Posting…" : "Post adjustment"}
      </button>
      <div aria-live="polite" className="text-xs">
        {state.kind === "error" && (
          <p className="text-rose-700 dark:text-rose-400">{state.message}</p>
        )}
      </div>
    </form>
  );
}
