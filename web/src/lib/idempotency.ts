import { useCallback, useMemo, useRef } from "react";

// Every mutating request carries an Idempotency-Key (ADR-0005), so a retry on
// a flaky connection answers as the first try did and records nothing twice.
export const idempotencyHeader = "Idempotency-Key";

// One key per submission of a form: the same key while the same submission is
// retried, a fresh one once it has gone through or the form has changed (the
// API refuses a key reused for a different request).
export function useIdempotencyKey() {
  const key = useRef<string | null>(null);

  const current = useCallback(() => {
    key.current ??= crypto.randomUUID();
    return key.current;
  }, []);

  const reset = useCallback(() => {
    key.current = null;
  }, []);

  return useMemo(() => ({ current, reset }), [current, reset]);
}
