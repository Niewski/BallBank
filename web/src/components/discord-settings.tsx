"use client";

import { useAuth0 } from "@auth0/auth0-react";
import { useEffect, useState, type FormEvent } from "react";
import { apiBaseUrl } from "@/lib/config";
import { problemMessage, unreachableMessage } from "@/lib/problem";

// What the API knows of the league's Discord connection. The webhook itself is
// never sent back: only its last few characters, to recognise it by.
type DiscordSettingsReading = {
  connected: boolean;
  webhookTail: string | null;
  announcePayments: boolean;
  postDigest: boolean;
};

type LoadState =
  | { kind: "loading" }
  | { kind: "ok"; settings: DiscordSettingsReading }
  | { kind: "error"; message: string };

type SaveState =
  | { kind: "idle" }
  | { kind: "working" }
  | { kind: "error"; message: string };

// Where the league's announcements go: a Discord channel's webhook, and what is
// announced in it. For a treasurer, on the dashboard.
export function DiscordSettings({ leagueId }: { leagueId: string }) {
  const { getAccessTokenSilently } = useAuth0();
  const [state, setState] = useState<LoadState>({ kind: "loading" });

  const path = `${apiBaseUrl}/leagues/${encodeURIComponent(leagueId)}/notifications/discord`;

  useEffect(() => {
    const controller = new AbortController();

    getAccessTokenSilently()
      .then((token) =>
        fetch(path, {
          headers: { Authorization: `Bearer ${token}` },
          signal: controller.signal,
        }),
      )
      .then(async (response) => {
        if (!response.ok) {
          setState({ kind: "error", message: await problemMessage(response) });
          return;
        }
        setState({
          kind: "ok",
          settings: (await response.json()) as DiscordSettingsReading,
        });
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return;
        setState({ kind: "error", message: unreachableMessage(error) });
      });

    return () => controller.abort();
  }, [getAccessTokenSilently, path]);

  return (
    <section
      aria-labelledby="discord-heading"
      className="flex flex-col gap-3 rounded-lg border border-zinc-200 bg-white p-4 dark:border-zinc-800 dark:bg-zinc-950"
    >
      <h2
        id="discord-heading"
        className="font-medium text-zinc-950 dark:text-zinc-50"
      >
        Discord
      </h2>
      {state.kind === "loading" && (
        <p className="text-sm text-zinc-500">Loading…</p>
      )}
      {state.kind === "error" && (
        <p className="text-sm text-rose-700 dark:text-rose-400">
          {state.message}
        </p>
      )}
      {state.kind === "ok" && (
        <DiscordForm
          path={path}
          settings={state.settings}
          onChanged={(settings) => setState({ kind: "ok", settings })}
        />
      )}
    </section>
  );
}

function DiscordForm({
  path,
  settings,
  onChanged,
}: {
  path: string;
  settings: DiscordSettingsReading;
  onChanged: (settings: DiscordSettingsReading) => void;
}) {
  const { getAccessTokenSilently } = useAuth0();
  const [webhookUrl, setWebhookUrl] = useState("");
  const [announcePayments, setAnnouncePayments] = useState(
    settings.announcePayments,
  );
  const [postDigest, setPostDigest] = useState(settings.postDigest);
  const [state, setState] = useState<SaveState>({ kind: "idle" });
  const [saved, setSaved] = useState(false);

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setState({ kind: "working" });
    setSaved(false);

    try {
      const token = await getAccessTokenSilently();
      const response = await fetch(path, {
        method: "PUT",
        headers: {
          Authorization: `Bearer ${token}`,
          "Content-Type": "application/json",
        },
        // Without a webhook, only the flags change. With one, the API says
        // hello through it first and keeps it only if Discord takes it.
        body: JSON.stringify({
          webhookUrl: webhookUrl.trim() === "" ? null : webhookUrl.trim(),
          announcePayments,
          postDigest,
        }),
      });

      if (!response.ok) {
        setState({ kind: "error", message: await problemMessage(response) });
        return;
      }

      setWebhookUrl("");
      setState({ kind: "idle" });
      setSaved(true);
      onChanged((await response.json()) as DiscordSettingsReading);
    } catch (error: unknown) {
      setState({ kind: "error", message: unreachableMessage(error) });
    }
  }

  async function disconnect() {
    setState({ kind: "working" });
    setSaved(false);

    try {
      const token = await getAccessTokenSilently();
      const response = await fetch(path, {
        method: "DELETE",
        headers: { Authorization: `Bearer ${token}` },
      });

      if (!response.ok) {
        setState({ kind: "error", message: await problemMessage(response) });
        return;
      }

      setWebhookUrl("");
      setAnnouncePayments(false);
      setPostDigest(false);
      setState({ kind: "idle" });
      onChanged({
        connected: false,
        webhookTail: null,
        announcePayments: false,
        postDigest: false,
      });
    } catch (error: unknown) {
      setState({ kind: "error", message: unreachableMessage(error) });
    }
  }

  const working = state.kind === "working";
  const field =
    "min-w-0 rounded-md border border-zinc-300 bg-white px-2 py-1 text-zinc-950 dark:border-zinc-700 dark:bg-zinc-950 dark:text-zinc-50";

  return (
    <form
      onSubmit={save}
      aria-label="Discord settings"
      className="flex flex-col gap-3 text-sm"
    >
      <p role="status" className="text-zinc-600 dark:text-zinc-400">
        {settings.connected
          ? `Connected. The webhook ends in ${settings.webhookTail}.`
          : "Not connected. BallBank will not post anywhere."}
      </p>

      <label className="flex flex-col gap-0.5 text-xs text-zinc-500">
        {settings.connected ? "Replace the webhook URL" : "Webhook URL"}
        {/* A webhook is a password to the channel: not shown, not remembered. */}
        <input
          type="password"
          name="webhook"
          autoComplete="off"
          spellCheck={false}
          placeholder="Paste it from the channel's Integrations settings in Discord"
          value={webhookUrl}
          required={!settings.connected}
          onChange={(event) => setWebhookUrl(event.target.value)}
          className={field}
        />
      </label>

      <label className="flex items-center gap-2 text-zinc-700 dark:text-zinc-300">
        <input
          type="checkbox"
          name="announcePayments"
          checked={announcePayments}
          onChange={(event) => setAnnouncePayments(event.target.checked)}
        />
        Announce each confirmed payment
      </label>
      <label className="flex items-center gap-2 text-zinc-700 dark:text-zinc-300">
        <input
          type="checkbox"
          name="postDigest"
          checked={postDigest}
          onChange={(event) => setPostDigest(event.target.checked)}
        />
        Post the weekly digest
      </label>

      <div className="flex gap-2">
        <button
          type="submit"
          disabled={working}
          className="rounded-md bg-emerald-700 px-3 py-1 text-xs font-medium text-white hover:bg-emerald-800 disabled:opacity-50"
        >
          {working
            ? "Saving…"
            : settings.connected
              ? "Save"
              : "Connect Discord"}
        </button>
        {settings.connected && (
          <button
            type="button"
            disabled={working}
            onClick={disconnect}
            className="rounded-md px-3 py-1 text-xs font-medium text-rose-700 hover:bg-rose-50 disabled:opacity-50 dark:text-rose-400 dark:hover:bg-zinc-900"
          >
            Disconnect
          </button>
        )}
      </div>

      <div aria-live="polite" className="text-xs">
        {state.kind === "error" && (
          <p className="text-rose-700 dark:text-rose-400">{state.message}</p>
        )}
        {saved && <p className="text-emerald-700 dark:text-emerald-400">Saved.</p>}
      </div>
    </form>
  );
}
