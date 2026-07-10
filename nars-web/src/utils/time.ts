// ─── TIME UTILITIES ────────────────────────────────────────────────────────────
// Shared time-related helpers.

/** Promise-based delay so async functions can await timing dependencies. */
export function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}
