// ─── DEBUG UTILITY ────────────────────────────────────────────────────────────
// Centralized debug logging that respects environment settings.
// In development mode, logs to console. In production, logs are suppressed.

const DEBUG = import.meta.env?.DEV ?? false

/**
 * Debug logging function - only logs in development mode.
 * Use this instead of console.log for application debugging.
 */
export function debugLog(...args: unknown[]): void {
  if (DEBUG) {
    // eslint-disable-next-line no-console
    console.log(...args)
  }
}

/**
 * Debug error logging - only logs in development mode.
 * Use this instead of console.error for application errors that should be hidden in production.
 */
export function debugError(...args: unknown[]): void {
  if (DEBUG) {
    // eslint-disable-next-line no-console
    console.error(...args)
  }
}

/**
 * Debug warning logging - only logs in development mode.
 * Use this instead of console.warn for application warnings.
 */
export function debugWarn(...args: unknown[]): void {
  if (DEBUG) {
    // eslint-disable-next-line no-console
    console.warn(...args)
  }
}
