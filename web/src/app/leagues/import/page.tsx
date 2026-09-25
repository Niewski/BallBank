import Link from "next/link";
import { ImportLeague } from "@/components/import-league";
import { SignInProvider } from "@/components/sign-in-provider";

export default function ImportLeaguePage() {
  return (
    <div className="flex flex-1 flex-col items-center justify-center bg-zinc-50 px-6 py-24 font-sans dark:bg-black">
      <main className="flex w-full max-w-2xl flex-col gap-6">
        <h1 className="text-3xl font-semibold tracking-tight text-zinc-950 dark:text-zinc-50">
          Import a league
        </h1>
        <p className="text-zinc-600 dark:text-zinc-400">
          Type your Sleeper username to see your leagues for this season.
        </p>
        <SignInProvider>
          <ImportLeague />
        </SignInProvider>
        <Link
          href="/"
          className="self-start underline decoration-zinc-400 underline-offset-4"
        >
          Back to my leagues
        </Link>
      </main>
    </div>
  );
}
