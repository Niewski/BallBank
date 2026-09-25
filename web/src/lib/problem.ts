// The API explains refusals and Sleeper failures in a problem document's
// detail; anything else gets a generic message.
export async function problemMessage(response: Response): Promise<string> {
  try {
    const problem = (await response.json()) as { detail?: string };
    if (problem.detail) return problem.detail;
  } catch {
    // Not a problem document; fall through.
  }
  return `Something went wrong (API responded ${response.status}). Try again.`;
}

export function unreachableMessage(error: unknown): string {
  return error instanceof Error
    ? `Could not reach BallBank: ${error.message}`
    : "Could not reach BallBank.";
}
