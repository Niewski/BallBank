"use client";

import { useEffect, useState } from "react";
import { apiBaseUrl } from "@/lib/config";

type VersionInfo = { name: string; version: string };

type Status =
  | { kind: "loading" }
  | { kind: "ok"; info: VersionInfo }
  | { kind: "error"; message: string };

export function ApiStatus() {
  const [status, setStatus] = useState<Status>({ kind: "loading" });

  useEffect(() => {
    const controller = new AbortController();

    fetch(`${apiBaseUrl}/v1/version`, { signal: controller.signal })
      .then(async (response) => {
        if (!response.ok) {
          throw new Error(`API responded ${response.status}`);
        }
        return (await response.json()) as VersionInfo;
      })
      .then((info) => setStatus({ kind: "ok", info }))
      .catch((error: unknown) => {
        if (controller.signal.aborted) return;
        setStatus({
          kind: "error",
          message: error instanceof Error ? error.message : "Unknown error",
        });
      });

    return () => controller.abort();
  }, []);

  return (
    <section
      aria-live="polite"
      className="rounded-lg border border-zinc-200 bg-white p-5 font-mono text-sm dark:border-zinc-800 dark:bg-zinc-950"
    >
      <p className="mb-2 text-zinc-500">
        API {apiBaseUrl ? `at ${apiBaseUrl}` : "(NEXT_PUBLIC_API_URL not set)"}
      </p>
      {status.kind === "loading" && (
        <p className="text-zinc-700 dark:text-zinc-300">Checking…</p>
      )}
      {status.kind === "ok" && (
        <p className="text-emerald-700 dark:text-emerald-400">
          {status.info.name} {status.info.version} is up.
        </p>
      )}
      {status.kind === "error" && (
        <p className="text-rose-700 dark:text-rose-400">
          Not reachable: {status.message}
        </p>
      )}
    </section>
  );
}
