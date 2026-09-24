import { ApiStatus } from "@/components/api-status";

export default function Home() {
  return (
    <div className="flex flex-1 flex-col items-center justify-center bg-zinc-50 px-6 py-24 font-sans dark:bg-black">
      <main className="flex w-full max-w-2xl flex-col gap-10">
        <header className="flex flex-col gap-4">
          <p className="font-mono text-sm uppercase tracking-widest text-emerald-700 dark:text-emerald-400">
            BallBank
          </p>
          <h1 className="text-4xl font-semibold tracking-tight text-zinc-950 dark:text-zinc-50">
            The books for your league&apos;s dues.
          </h1>
          <p className="max-w-prose text-lg leading-8 text-zinc-600 dark:text-zinc-400">
            Who owes, who paid, who confirmed it, and who got paid out at the
            end of the season. Every entry is a recorded event, so the history
            is the audit trail. Money never moves through BallBank; it records
            the payments members make to each other.
          </p>
        </header>

        <ApiStatus />

        <footer className="text-sm text-zinc-500 dark:text-zinc-500">
          Built in the open during the 2026 season. Source on{" "}
          <a
            className="underline decoration-zinc-400 underline-offset-4 hover:text-zinc-900 dark:hover:text-zinc-100"
            href="https://github.com/Niewski/BallBank"
          >
            GitHub
          </a>
          .
        </footer>
      </main>
    </div>
  );
}
