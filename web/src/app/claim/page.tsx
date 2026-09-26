import Link from "next/link";
import { Suspense } from "react";
import { Claim } from "@/components/claim";
import { SignInProvider } from "@/components/sign-in-provider";

// An invite's link: /claim?league=&invite=. The static export has one page for
// every invite; which one comes from the query.
export default function ClaimPage() {
  return (
    <div className="flex flex-1 flex-col items-center justify-center bg-zinc-50 px-6 py-24 font-sans dark:bg-black">
      <main className="flex w-full max-w-xl flex-col gap-6">
        <SignInProvider>
          <Suspense fallback={<p className="text-zinc-500">Loading…</p>}>
            <Claim />
          </Suspense>
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
