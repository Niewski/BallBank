"use client";

import { Auth0Provider } from "@auth0/auth0-react";
import type { ReactNode } from "react";
import { auth0 } from "@/lib/config";

// Authorization code with PKCE against the BallBank API audience. Tokens are
// cached in localStorage with rotating refresh tokens so a reload keeps the
// session without a silent-auth iframe, which browsers blocking third-party
// cookies would break.
export function SignInProvider({ children }: { children: ReactNode }) {
  if (!auth0) {
    return (
      <p className="rounded-lg border border-amber-300 bg-amber-50 p-5 text-sm text-amber-900 dark:border-amber-800 dark:bg-amber-950 dark:text-amber-200">
        Sign-in is not configured for this build (NEXT_PUBLIC_AUTH0_DOMAIN,
        NEXT_PUBLIC_AUTH0_CLIENT_ID and NEXT_PUBLIC_AUTH0_AUDIENCE).
      </p>
    );
  }

  return (
    <Auth0Provider
      domain={auth0.domain}
      clientId={auth0.clientId}
      authorizationParams={{
        audience: auth0.audience,
        redirect_uri:
          typeof window === "undefined" ? undefined : window.location.origin,
      }}
      cacheLocation="localstorage"
      useRefreshTokens
    >
      {children}
    </Auth0Provider>
  );
}
