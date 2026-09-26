"use client";

import { useAuth0 } from "@auth0/auth0-react";
import { useState, type ChangeEvent, type FormEvent } from "react";
import { apiBaseUrl } from "@/lib/config";
import { idempotencyHeader, useIdempotencyKey } from "@/lib/idempotency";
import { problemMessage, unreachableMessage } from "@/lib/problem";

type OpenSeasonState =
  | { kind: "idle" }
  | { kind: "opening" }
  | { kind: "error"; message: string };

// A treasurer opens the season with a label, the dues amount and the due
// date. Every current member is assessed the dues in one step. Opening a
// season that is already open changes nothing, so a retry cannot double-assess.
// Submitting again after a failure retries the same submission, under the same
// Idempotency-Key; changing a field makes it a new one.
export function OpenSeason({
  leagueId,
  onOpened,
}: {
  leagueId: string;
  onOpened: () => void;
}) {
  const { getAccessTokenSilently } = useAuth0();
  const [label, setLabel] = useState("");
  const [duesAmount, setDuesAmount] = useState("");
  const [dueDate, setDueDate] = useState("");
  const [state, setState] = useState<OpenSeasonState>({ kind: "idle" });
  const idempotencyKey = useIdempotencyKey();

  // A changed field makes the next submission a new one.
  function edit(set: (value: string) => void) {
    return (event: ChangeEvent<HTMLInputElement>) => {
      idempotencyKey.reset();
      set(event.target.value);
    };
  }

  async function open(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setState({ kind: "opening" });

    try {
      const token = await getAccessTokenSilently();
      const response = await fetch(
        `${apiBaseUrl}/leagues/${encodeURIComponent(leagueId)}/seasons`,
        {
          method: "POST",
          headers: {
            Authorization: `Bearer ${token}`,
            "Content-Type": "application/json",
            [idempotencyHeader]: idempotencyKey.current(),
          },
          body: JSON.stringify({
            label,
            duesAmount: Number(duesAmount),
            dueDate,
          }),
        },
      );

      if (!response.ok) {
        setState({ kind: "error", message: await problemMessage(response) });
        return;
      }

      idempotencyKey.reset();
      setState({ kind: "idle" });
      onOpened();
    } catch (error: unknown) {
      setState({ kind: "error", message: unreachableMessage(error) });
    }
  }

  const opening = state.kind === "opening";
  const field =
    "min-w-0 rounded-md border border-zinc-300 bg-white px-2 py-1 text-zinc-950 dark:border-zinc-700 dark:bg-zinc-950 dark:text-zinc-50";

  return (
    <form
      onSubmit={open}
      aria-label="Open the season"
      className="flex flex-col gap-3 rounded-lg border border-zinc-200 bg-white p-4 dark:border-zinc-800 dark:bg-zinc-950"
    >
      <p className="text-sm font-medium text-zinc-950 dark:text-zinc-50">
        Open the season
      </p>
      <div className="flex flex-wrap gap-3">
        <label className="flex flex-col gap-0.5 text-xs text-zinc-500">
          Label
          <input
            name="label"
            required
            autoComplete="off"
            value={label}
            onChange={edit(setLabel)}
            className={field}
          />
        </label>
        <label className="flex flex-col gap-0.5 text-xs text-zinc-500">
          Dues
          <input
            name="duesAmount"
            type="number"
            min="0.01"
            step="0.01"
            required
            value={duesAmount}
            onChange={edit(setDuesAmount)}
            className={field}
          />
        </label>
        <label className="flex flex-col gap-0.5 text-xs text-zinc-500">
          Due date
          <input
            name="dueDate"
            type="date"
            required
            value={dueDate}
            onChange={edit(setDueDate)}
            className={field}
          />
        </label>
      </div>
      <button
        type="submit"
        disabled={opening}
        className="self-start rounded-md bg-emerald-700 px-3 py-1.5 text-xs font-medium text-white hover:bg-emerald-800 disabled:opacity-50"
      >
        {opening ? "Opening…" : "Open season"}
      </button>
      <div aria-live="polite" className="text-xs">
        {state.kind === "error" && (
          <p className="text-rose-700 dark:text-rose-400">{state.message}</p>
        )}
      </div>
    </form>
  );
}
