import Link from "next/link";
import { Suspense } from "react";
import { Dashboard } from "@/components/dashboard";
import { SignInProvider } from "@/components/sign-in-provider";

// A season's treasurer dashboard: /dashboard?league=&season=. The static
// export has one page for every season; which one comes from the query.
export default function DashboardPage() {
  return (
    <div className="flex flex-1 flex-col items-center justify-center bg-zinc-50 px-6 py-24 font-sans dark:bg-black">
      <main className="flex w-full max-w-3xl flex-col gap-6">
        <SignInProvider>
          <Suspense fallback={<p className="text-zinc-500">Loading…</p>}>
            <Dashboard />
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
