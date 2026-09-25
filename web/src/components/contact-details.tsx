"use client";

import { useAuth0 } from "@auth0/auth0-react";
import { useState, type FormEvent } from "react";
import { apiBaseUrl } from "@/lib/config";
import { problemMessage, unreachableMessage } from "@/lib/problem";

export type Contact = {
  email: string | null;
  // E.164, e.g. +15550100000.
  phone: string | null;
  discordUsername: string | null;
};

// +15550100000 as (555) 010-0000; anything else as it came.
function formatPhone(phone: string): string {
  const us = /^\+1(\d{3})(\d{3})(\d{4})$/.exec(phone);
  return us ? `(${us[1]}) ${us[2]}-${us[3]}` : phone;
}

// How to reach one member, for a treasurer or the member themselves, with a
// form to change it. The API only sends contact details to those who may
// change them, so whoever sees this cell may edit it.
export function ContactDetails({
  leagueId,
  memberId,
  teamName,
  claimed,
  contact,
  onSaved,
}: {
  leagueId: string;
  memberId: string;
  teamName: string;
  claimed: boolean;
  contact: Contact;
  onSaved: () => void;
}) {
  const [editing, setEditing] = useState(false);

  if (editing) {
    return (
      <ContactForm
        leagueId={leagueId}
        memberId={memberId}
        teamName={teamName}
        claimed={claimed}
        contact={contact}
        onDone={(saved) => {
          setEditing(false);
          if (saved) onSaved();
        }}
      />
    );
  }

  const nothing = !contact.email && !contact.phone && !contact.discordUsername;

  return (
    <div className="flex flex-col items-start gap-1">
      {nothing ? (
        <span className="text-zinc-400">None recorded</span>
      ) : (
        <ul className="flex flex-col gap-0.5 text-zinc-700 dark:text-zinc-300">
          {contact.email && <li>{contact.email}</li>}
          {contact.phone && (
            <li>
              <a href={`tel:${contact.phone}`} className="hover:underline">
                {formatPhone(contact.phone)}
              </a>
            </li>
          )}
          {contact.discordUsername && <li>@{contact.discordUsername}</li>}
        </ul>
      )}
      <button
        type="button"
        onClick={() => setEditing(true)}
        aria-label={`Edit contact details for ${teamName}`}
        className="text-xs font-medium text-emerald-700 hover:underline dark:text-emerald-400"
      >
        {nothing ? "Add" : "Edit"}
      </button>
    </div>
  );
}

type SaveState =
  | { kind: "idle" }
  | { kind: "saving" }
  | { kind: "error"; message: string };

function ContactForm({
  leagueId,
  memberId,
  teamName,
  claimed,
  contact,
  onDone,
}: {
  leagueId: string;
  memberId: string;
  teamName: string;
  claimed: boolean;
  contact: Contact;
  onDone: (saved: boolean) => void;
}) {
  const { getAccessTokenSilently } = useAuth0();
  const [email, setEmail] = useState(contact.email ?? "");
  const [phone, setPhone] = useState(
    contact.phone ? formatPhone(contact.phone) : "",
  );
  const [discordUsername, setDiscordUsername] = useState(
    contact.discordUsername ?? "",
  );
  const [state, setState] = useState<SaveState>({ kind: "idle" });

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setState({ kind: "saving" });

    try {
      const token = await getAccessTokenSilently();
      const response = await fetch(
        `${apiBaseUrl}/leagues/${encodeURIComponent(leagueId)}/members/${encodeURIComponent(memberId)}/contact`,
        {
          method: "PUT",
          headers: {
            Authorization: `Bearer ${token}`,
            "Content-Type": "application/json",
          },
          // Replaces what was recorded: an empty field is removed.
          body: JSON.stringify({ email, phone, discordUsername }),
        },
      );

      if (!response.ok) {
        setState({ kind: "error", message: await problemMessage(response) });
        return;
      }

      onDone(true);
    } catch (error: unknown) {
      setState({ kind: "error", message: unreachableMessage(error) });
    }
  }

  const field =
    "min-w-0 rounded-md border border-zinc-300 bg-white px-2 py-1 text-zinc-950 dark:border-zinc-700 dark:bg-zinc-950 dark:text-zinc-50";
  const saving = state.kind === "saving";

  return (
    <form
      onSubmit={save}
      aria-label={`Contact details for ${teamName}`}
      className="flex min-w-56 flex-col gap-2"
    >
      <label className="flex flex-col gap-0.5 text-xs text-zinc-500">
        Email{claimed ? "" : " (optional)"}
        <input
          type="email"
          name="email"
          autoComplete="off"
          required={claimed}
          value={email}
          onChange={(event) => setEmail(event.target.value)}
          className={field}
        />
      </label>
      <label className="flex flex-col gap-0.5 text-xs text-zinc-500">
        US phone (optional)
        <input
          type="tel"
          name="phone"
          autoComplete="off"
          placeholder="(555) 010-0000"
          value={phone}
          onChange={(event) => setPhone(event.target.value)}
          className={field}
        />
      </label>
      <label className="flex flex-col gap-0.5 text-xs text-zinc-500">
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
      <p className="text-xs text-zinc-500">
        Empty a field to remove it.
        {claimed ? "" : " Empty them all to keep nothing."}
      </p>
      <div className="flex gap-2">
        <button
          type="submit"
          disabled={saving}
          className="rounded-md bg-emerald-700 px-3 py-1 text-xs font-medium text-white hover:bg-emerald-800 disabled:opacity-50"
        >
          {saving ? "Saving…" : "Save"}
        </button>
        <button
          type="button"
          disabled={saving}
          onClick={() => onDone(false)}
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
