// Public build-time settings. Next.js inlines NEXT_PUBLIC_* at build, so each must
// be read by its full name. None of them is a secret: they identify the Auth0
// application and API, and every browser that loads the site sees them.

export const apiBaseUrl = process.env.NEXT_PUBLIC_API_URL ?? "";

const domain = process.env.NEXT_PUBLIC_AUTH0_DOMAIN;
const clientId = process.env.NEXT_PUBLIC_AUTH0_CLIENT_ID;
const audience = process.env.NEXT_PUBLIC_AUTH0_AUDIENCE;

export type Auth0Settings = { domain: string; clientId: string; audience: string };

/** Auth0 settings, or null when the build was made without them. */
export const auth0: Auth0Settings | null =
  domain && clientId && audience ? { domain, clientId, audience } : null;
