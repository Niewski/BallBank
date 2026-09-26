"use client";

import { useAuth0 } from "@auth0/auth0-react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { useEffect, useState, type FormEvent } from "react";
import { formatPhone, type Contact } from "@/components/contact-details";
import { apiBaseUrl } from "@/lib/config";
import { problemMessage, unreachableMessage } from "@/lib/problem";

type InviteToClaim = {
  leagueId: string;
  name: string;
  season: string;
  memberId: string;
  teamName: string;
  sleeperDisplayName: string | null;
  // The caller already holds this member.
  yours: boolean;
  // Why the caller cannot claim with this invite; null when they can.
  refusal: string | null;
  // What the treasurer recorded; null unless the caller can claim.
  contact: Contact | null;
};

type InviteState =
  | { kind: "loading" }
  | { kind: "ok"; invite: InviteToClaim }
  | { kind: "error"; message: string };

// Whoever opens an invite link becomes the member it was issued for. Signing
// in comes first, and brings them back to this same link.
export function Claim() {
  const { isLoading, isAuthenticated, loginWithRedirect } = useAuth0();
  const params = useSearchParams();
  const leagueId = params.get("league");
  const inviteId = params.get("invite");

  if (!leagueId || !inviteId) {
    return (
      <p className="text-zinc-700 dark:text-zinc-300">
        This link is incomplete. Ask your treasurer to send it again.
      </p>
    );
  }

  if (isLoading) {
    return <p className="text-zinc-500">Signing you in…</p>;
  }

  if (!isAuthenticated) {
    return (
      <div className="flex flex-col gap-4">
        <h1 className="text-3xl font-semibold tracking-tight text-zinc-950 dark:text-zinc-50">
          You&apos;re invited to a league
        </h1>
        <p className="text-zinc-600 dark:text-zinc-400">
          Sign in to see which league and team, and to claim it. You will come
          back here once you are signed in.
        </p>
        <button
          type="button"
          className="self-start rounded-md bg-emerald-700 px-5 py-2.5 font-medium text-white hover:bg-emerald-800"
          onClick={() =>
            loginWithRedirect({
              appState: {
                returnTo: `${window.location.pathname}${window.location.search}`,
              },
            })
          }
        >
          Sign in
        </button>
      </div>
    );
  }

  return (
    <OpenedInvite
      key={`${leagueId}/${inviteId}`}
      leagueId={leagueId}
      inviteId={inviteId}
    />
  );
}

function OpenedInvite({
  leagueId,
  inviteId,
}: {
  leagueId: string;
  inviteId: string;
}) {
  const { getAccessTokenSilently } = useAuth0();
  const [state, setState] = useState<InviteState>({ kind: "loading" });

  useEffect(() => {
    const controller = new AbortController();

    getAccessTokenSilently()
      .then((token) =>
        fetch(
          `${apiBaseUrl}/leagues/${encodeURIComponent(leagueId)}/invites/${encodeURIComponent(inviteId)}`,
          {
            headers: { Authorization: `Bearer ${token}` },
            signal: controller.signal,
          },
        ),
      )
      .then(async (response) => {
        if (!response.ok) {
          setState({ kind: "error", message: await problemMessage(response) });
          return;
        }
        setState({
          kind: "ok",
          invite: (await response.json()) as InviteToClaim,
        });
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return;
        setState({ kind: "error", message: unreachableMessage(error) });
      });

    return () => controller.abort();
  }, [getAccessTokenSilently, leagueId, inviteId]);

  if (state.kind === "loading") {
    return <p className="text-zinc-500">Opening your invite…</p>;
  }

  if (state.kind === "error") {
    return <p className="text-rose-700 dark:text-rose-400">{state.message}</p>;
  }

  const { invite } = state;
  const leagueLink = `/league?id=${invite.leagueId}`;

  return (
    <section className="flex flex-col gap-6">
      <header className="flex flex-col gap-1">
        <p className="text-sm text-zinc-500">
          {invite.name} · {invite.season} season
        </p>
        <h1 className="text-3xl font-semibold tracking-tight text-zinc-950 dark:text-zinc-50">
          {invite.teamName}
        </h1>
        {invite.sleeperDisplayName && (
          <p className="text-sm text-zinc-500">
            {invite.sleeperDisplayName} on Sleeper
          </p>
        )}
      </header>

      {invite.yours ? (
        <div className="flex flex-col gap-3">
          <p className="text-zinc-700 dark:text-zinc-300">
            This team is already yours.
          </p>
          <Link
            href={leagueLink}
            className="self-start rounded-md bg-emerald-700 px-4 py-2 text-sm font-medium text-white hover:bg-emerald-800"
          >
            Go to {invite.name}
          </Link>
        </div>
      ) : invite.refusal ? (
        <p className="text-rose-700 dark:text-rose-400">{invite.refusal}</p>
      ) : (
        <ClaimForm invite={invite} inviteId={inviteId} />
      )}
    </section>
  );
}

type ClaimState =
  | { kind: "idle" }
  | { kind: "claiming" }
  | { kind: "error"; message: string };

function ClaimForm({
  invite,
  inviteId,
}: {
  invite: InviteToClaim;
  inviteId: string;
}) {
  const { getAccessTokenSilently, user } = useAuth0();
  const router = useRouter();
  const recorded = invite.contact;
  const [displayName, setDisplayName] = useState(
    invite.sleeperDisplayName ?? "",
  );
  // What the treasurer recorded, else the email this person signed in with.
  const [email, setEmail] = useState(recorded?.email ?? user?.email ?? "");
  const [phone, setPhone] = useState(
    recorded?.phone ? formatPhone(recorded.phone) : "",
  );
  const [discordUsername, setDiscordUsername] = useState(
    recorded?.discordUsername ?? "",
  );
  const [state, setState] = useState<ClaimState>({ kind: "idle" });

  async function claim(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setState({ kind: "claiming" });

    try {
      const token = await getAccessTokenSilently();
      const response = await fetch(
        `${apiBaseUrl}/leagues/${encodeURIComponent(invite.leagueId)}/claims`,
        {
          method: "POST",
          headers: {
            Authorization: `Bearer ${token}`,
            "Content-Type": "application/json",
          },
          body: JSON.stringify({
            inviteId,
            displayName,
            email,
            phone,
            discordUsername,
          }),
        },
      );

      if (!response.ok) {
        setState({ kind: "error", message: await problemMessage(response) });
        return;
      }

      router.push(`/league?id=${invite.leagueId}`);
    } catch (error: unknown) {
      setState({ kind: "error", message: unreachableMessage(error) });
    }
  }

  const field =
    "rounded-md border border-zinc-300 bg-white px-3 py-2 text-zinc-950 dark:border-zinc-700 dark:bg-zinc-950 dark:text-zinc-50";
  const label = "flex flex-col gap-1 text-sm text-zinc-600 dark:text-zinc-400";
  const claiming = state.kind === "claiming";

  return (
    <form onSubmit={claim} className="flex flex-col gap-4">
      <p className="text-zinc-600 dark:text-zinc-400">
        Claim this team to keep its books in {invite.name}. Only the treasurer
        sees how to reach you.
      </p>
      <label className={label}>
        Your name in this league
        <input
          name="displayName"
          required
          autoComplete="nickname"
          value={displayName}
          onChange={(event) => setDisplayName(event.target.value)}
          className={field}
        />
      </label>
      <label className={label}>
        Email
        <input
          type="email"
          name="email"
          required
          autoComplete="email"
          value={email}
          onChange={(event) => setEmail(event.target.value)}
          className={field}
        />
      </label>
      <label className={label}>
        US phone (optional)
        <input
          type="tel"
          name="phone"
          autoComplete="tel-national"
          placeholder="(555) 010-0000"
          value={phone}
          onChange={(event) => setPhone(event.target.value)}
          className={field}
        />
      </label>
      <label className={label}>
        Discord username (optional)
        <input
          name="discord"
          autoComplete="off"
          autoCapitalize="none"
          spellCheck={false}
          value={discordUsername}
          onChange={(event) => setDiscordUsername(event.target.value)}
          className={field}
        />
      </label>
      <button
        type="submit"
        disabled={claiming}
        className="self-start rounded-md bg-emerald-700 px-5 py-2.5 font-medium text-white hover:bg-emerald-800 disabled:opacity-50"
      >
        {claiming ? "Claiming…" : `Claim ${invite.teamName}`}
      </button>
      <div aria-live="polite">
        {state.kind === "error" && (
          <p className="text-rose-700 dark:text-rose-400">{state.message}</p>
        )}
      </div>
    </form>
  );
}
