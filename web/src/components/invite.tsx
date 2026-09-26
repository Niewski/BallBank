"use client";

import { useAuth0 } from "@auth0/auth0-react";
import { useState } from "react";
import { apiBaseUrl } from "@/lib/config";
import { problemMessage, unreachableMessage } from "@/lib/problem";

type IssuedInvite = {
  inviteId: string;
  memberId: string;
  // The claim page, relative to wherever this web app is served.
  url: string;
  expiresAt: string;
};

type InviteState =
  | { kind: "idle" }
  | { kind: "issuing" }
  | { kind: "issued"; link: string; expiresAt: Date; copied: boolean }
  // The invite that failed, so trying again sends the same one.
  | { kind: "error"; message: string; inviteId: string };

// A treasurer invites whoever should hold an unclaimed member. BallBank sends
// nothing: the treasurer copies the link, or texts it from their own phone.
// Issuing a new link voids the one before.
export function Invite({
  leagueId,
  leagueName,
  memberId,
  teamName,
  phone,
}: {
  leagueId: string;
  leagueName: string;
  memberId: string;
  teamName: string;
  // E.164, e.g. +15550100000; null when none is recorded.
  phone: string | null;
}) {
  const { getAccessTokenSilently } = useAuth0();
  const [state, setState] = useState<InviteState>({ kind: "idle" });

  async function issue(inviteId: string) {
    setState({ kind: "issuing" });

    try {
      const token = await getAccessTokenSilently();
      const response = await fetch(
        `${apiBaseUrl}/leagues/${encodeURIComponent(leagueId)}/members/${encodeURIComponent(memberId)}/invites`,
        {
          method: "POST",
          headers: {
            Authorization: `Bearer ${token}`,
            "Content-Type": "application/json",
          },
          // The invite's id, so a retried request issues nothing new.
          body: JSON.stringify({ inviteId }),
        },
      );

      if (!response.ok) {
        setState({
          kind: "error",
          message: await problemMessage(response),
          inviteId,
        });
        return;
      }

      const invite = (await response.json()) as IssuedInvite;
      setState({
        kind: "issued",
        link: new URL(invite.url, window.location.origin).toString(),
        expiresAt: new Date(invite.expiresAt),
        copied: false,
      });
    } catch (error: unknown) {
      setState({
        kind: "error",
        message: unreachableMessage(error),
        inviteId,
      });
    }
  }

  async function copy(link: string, expiresAt: Date) {
    try {
      await navigator.clipboard.writeText(link);
      setState({ kind: "issued", link, expiresAt, copied: true });
    } catch {
      // The link is on screen to copy by hand.
    }
  }

  const secondary =
    "rounded-md border border-zinc-300 px-2 py-1 text-xs font-medium text-zinc-700 hover:bg-zinc-100 disabled:opacity-50 dark:border-zinc-700 dark:text-zinc-300 dark:hover:bg-zinc-900";

  if (state.kind !== "issued") {
    return (
      <div className="flex flex-col items-start gap-1">
        <button
          type="button"
          onClick={() =>
            issue(state.kind === "error" ? state.inviteId : crypto.randomUUID())
          }
          disabled={state.kind === "issuing"}
          aria-label={`Invite someone to claim ${teamName}`}
          className="text-xs font-medium text-emerald-700 hover:underline disabled:opacity-50 dark:text-emerald-400"
        >
          {state.kind === "issuing" ? "Inviting…" : "Invite"}
        </button>
        <div aria-live="polite" className="text-xs">
          {state.kind === "error" && (
            <p className="text-rose-700 dark:text-rose-400">{state.message}</p>
          )}
        </div>
      </div>
    );
  }

  const { link, expiresAt, copied } = state;
  const text = `You're invited to claim ${teamName} in ${leagueName} on BallBank: ${link}`;

  return (
    <div className="flex min-w-56 flex-col items-start gap-2">
      <input
        readOnly
        value={link}
        aria-label={`Invite link for ${teamName}`}
        onFocus={(event) => event.target.select()}
        className="w-full min-w-0 rounded-md border border-zinc-300 bg-zinc-50 px-2 py-1 text-xs text-zinc-700 dark:border-zinc-700 dark:bg-zinc-900 dark:text-zinc-300"
      />
      <div className="flex flex-wrap gap-2">
        <button
          type="button"
          onClick={() => copy(link, expiresAt)}
          className={secondary}
        >
          {copied ? "Copied" : "Copy link"}
        </button>
        {phone ? (
          // Opens the treasurer's own messaging app; BallBank sends nothing.
          <a
            href={`sms:${phone}?body=${encodeURIComponent(text)}`}
            className={secondary}
          >
            Text invite
          </a>
        ) : (
          <button
            type="button"
            disabled
            title="Record a phone number for this member to text the invite"
            className={secondary}
          >
            Text invite
          </button>
        )}
        <button
          type="button"
          onClick={() => issue(crypto.randomUUID())}
          title="Issues a new link and voids this one"
          className={secondary}
        >
          New link
        </button>
      </div>
      <p className="text-xs text-zinc-500">
        Expires{" "}
        {expiresAt.toLocaleDateString(undefined, {
          month: "short",
          day: "numeric",
        })}
        . A new link voids this one.
      </p>
    </div>
  );
}
