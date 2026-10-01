// The API explains refusals, Sleeper failures and a spent request budget in a
// problem document's detail; anything else gets a generic message. A 429 with
// no document (from a proxy in front of the API) still says when to come back.
export async function problemMessage(response: Response): Promise<string> {
  try {
    const problem = (await response.json()) as { detail?: string };
    if (problem.detail) return problem.detail;
  } catch {
    // Not a problem document; fall through.
  }
  if (response.status === 429) {
    const seconds = Number(response.headers.get("Retry-After"));
    return seconds > 0
      ? `Too many requests. Try again in ${seconds} seconds.`
      : "Too many requests. Try again in a moment.";
  }
  return `Something went wrong (API responded ${response.status}). Try again.`;
}

export function unreachableMessage(error: unknown): string {
  return error instanceof Error
    ? `Could not reach BallBank: ${error.message}`
    : "Could not reach BallBank.";
}

// The problem type of a command sent against a version of an account that
// someone has changed since (ADR-0005): reload it and try again.
export const versionConflictType = "version-conflict";
