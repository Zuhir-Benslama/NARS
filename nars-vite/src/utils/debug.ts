// ─── DEBUG UTILITY ────────────────────────────────────────────────────────────
// Centralized debug logging that respects environment settings.
// In development mode, logs to console. In production, logs are suppressed.
/* eslint-disable no-console */
/* eslint-disable @typescript-eslint/no-explicit-any */

const DEBUG = import.meta.env?.DEV ?? false

/**
 * Debug logging function - only logs in development mode.
 * Use this instead of console.log for application debugging.
 */
export function debugLog(...args: any[]): void {
  if (DEBUG) {
    console.log(...args)
  }
}

/**
 * Debug error logging - only logs in development mode.
 * Use this instead of console.error for application errors that should be hidden in production.
 */
export function debugError(...args: any[]): void {
  if (DEBUG) {
    console.error(...args)
  }
}

/**
 * Debug warning logging - only logs in development mode.
 * Use this instead of console.warn for application warnings.
 */
export function debugWarn(...args: any[]): void {
  if (DEBUG) {
    console.warn(...args)
  }
}

/**
 * Debug info logging - only logs in development mode.
 * Use this instead of console.info for application info messages.
 */
export function debugInfo(...args: any[]): void {
  if (DEBUG) {
    console.info(...args)
  }
}

/**
 * Check if debug mode is enabled.
 * Useful for conditional debug behavior.
 */
export function isDebugEnabled(): boolean {
  return DEBUG
}
