// ─── ERROR HANDLING ───────────────────────────────────────────────────────────
// Centralized error handling with error codes, retry logic, and contextual logging.

import { isDev } from "../config"
import { captureError } from "./logger"

// ─── ERROR CATEGORIES ─────────────────────────────────────────────────────────

export enum ErrorCode {
  NETWORK = "NETWORK_ERROR",
  VALIDATION = "VALIDATION_ERROR",
  AUTH = "AUTH_ERROR",
  NOT_FOUND = "NOT_FOUND",
  SERVER = "SERVER_ERROR",
  TIMEOUT = "TIMEOUT_ERROR",
  PERMISSION = "PERMISSION_ERROR",
  CONFLICT = "CONFLICT_ERROR",
  UNKNOWN = "UNKNOWN_ERROR",
}

// ─── ERROR CONTEXT ────────────────────────────────────────────────────────────

export interface ErrorContext {
  phase?: string
  featureType?: string
  action?: string
  dbId?: string
  url?: string
  method?: string
  status?: number
  code?: string
}

// ─── NARS ERROR CLASS ─────────────────────────────────────────────────────────

export class NarsError extends Error {
  public readonly code: ErrorCode
  public readonly context: ErrorContext
  public readonly timestamp: Date

  constructor(
    code: ErrorCode,
    message: string,
    context: ErrorContext = {},
    public readonly cause?: unknown,
  ) {
    super(message)
    this.name = "NarsError"
    this.code = code
    this.context = context
    this.timestamp = new Date()

    // Capture stack trace (V8 environments)
    if (Error.captureStackTrace) {
      Error.captureStackTrace(this, NarsError)
    }
  }

  // Get a user-friendly message
  getUserMessage(): string {
    switch (this.code) {
      case ErrorCode.NETWORK:
        return "Network error. Please check your connection and try again."
      case ErrorCode.VALIDATION:
        return "Please check your input and try again."
      case ErrorCode.AUTH:
        return "Authentication required. Please log in again."
      case ErrorCode.NOT_FOUND:
        return "Feature not found. It may have been deleted."
      case ErrorCode.SERVER:
        return "Server error. Please try again later."
      case ErrorCode.TIMEOUT:
        return "Request timed out. Please try again."
      case ErrorCode.PERMISSION:
        return "You do not have permission to perform this action."
      case ErrorCode.CONFLICT:
        return "This resource has been modified by another user. Please refresh."
      default:
        return "An unexpected error occurred. Please try again."
    }
  }

  // Get technical details for logging
  getTechnicalDetails(): string {
    const parts = [`[${this.code}]`, this.message]
    if (Object.keys(this.context).length > 0) {
      parts.push(`Context: ${JSON.stringify(this.context)}`)
    }
    if (this.cause) {
      parts.push(`Cause: ${this.cause instanceof Error ? this.cause.message : String(this.cause)}`)
    }
    return parts.join(" | ")
  }
}

// ─── ERROR FACTORY FUNCTIONS ──────────────────────────────────────────────────

export function createNetworkError(
  message: string,
  context: ErrorContext = {},
  cause?: unknown,
): NarsError {
  return new NarsError(ErrorCode.NETWORK, message, context, cause)
}

export function createValidationError(
  message: string,
  context: ErrorContext = {},
  cause?: unknown,
): NarsError {
  return new NarsError(ErrorCode.VALIDATION, message, context, cause)
}

export function createAuthError(
  message: string,
  context: ErrorContext = {},
  cause?: unknown,
): NarsError {
  return new NarsError(ErrorCode.AUTH, message, context, cause)
}

export function createNotFoundError(
  message: string,
  context: ErrorContext = {},
  cause?: unknown,
): NarsError {
  return new NarsError(ErrorCode.NOT_FOUND, message, context, cause)
}

export function createServerError(
  message: string,
  context: ErrorContext = {},
  cause?: unknown,
): NarsError {
  return new NarsError(ErrorCode.SERVER, message, context, cause)
}

export function createTimeoutError(
  message: string,
  context: ErrorContext = {},
  cause?: unknown,
): NarsError {
  return new NarsError(ErrorCode.TIMEOUT, message, context, cause)
}

export function createPermissionError(
  message: string,
  context: ErrorContext = {},
  cause?: unknown,
): NarsError {
  return new NarsError(ErrorCode.PERMISSION, message, context, cause)
}

export function createConflictError(
  message: string,
  context: ErrorContext = {},
  cause?: unknown,
): NarsError {
  return new NarsError(ErrorCode.CONFLICT, message, context, cause)
}

// ─── RETRY LOGIC ──────────────────────────────────────────────────────────────

export interface RetryOptions {
  maxRetries?: number
  baseDelay?: number
  maxDelay?: number
  shouldRetry?: (error: NarsError) => boolean
}

const DEFAULT_RETRY_OPTIONS: Required<RetryOptions> = {
  maxRetries: 3,
  baseDelay: 1000,
  maxDelay: 10000,
  shouldRetry: (error) => error.code === ErrorCode.NETWORK || error.code === ErrorCode.TIMEOUT,
}

// Exponential backoff with jitter
function calculateDelay(attempt: number, baseDelay: number, maxDelay: number): number {
  const exponential = baseDelay * 2 ** attempt
  const jitter = Math.random() * 0.3 * exponential
  return Math.min(exponential + jitter, maxDelay)
}

/**
 * Execute an async function with retry logic.
 * @param fn - Async function to execute
 * @param options - Retry configuration
 * @param onRetry - Optional callback invoked on each retry
 */
export async function withRetry<T>(
  fn: () => Promise<T>,
  options: RetryOptions = {},
  onRetry?: (error: NarsError, attempt: number) => void,
): Promise<T> {
  const { maxRetries, baseDelay, maxDelay, shouldRetry } = {
    ...DEFAULT_RETRY_OPTIONS,
    ...options,
  }

  let lastError: NarsError | null = null

  for (let attempt = 0; attempt <= maxRetries; attempt++) {
    try {
      return await fn()
    } catch (error) {
      // Caller-initiated aborts are never retryable — rethrow as-is so the
      // caller's AbortError detection (e.g. DOMException name checks) works.
      if (error instanceof DOMException && error.name === "AbortError") {
        throw error
      }

      const narsError =
        error instanceof NarsError
          ? error
          : error instanceof TypeError || error instanceof DOMException
            ? createNetworkError(
                error instanceof Error ? error.message : "Unknown error",
                {},
                error,
              )
            : null

      if (narsError === null) {
        // Not a network-related error — re-throw as-is so programming
        // errors (undefined method calls, typos, etc.) are not masked
        // as transient network issues that trigger unnecessary retries.
        throw error
      }

      lastError = narsError

      if (!shouldRetry(narsError) || attempt === maxRetries) {
        throw narsError
      }

      onRetry?.(narsError, attempt + 1)

      const delay = calculateDelay(attempt, baseDelay, maxDelay)
      await new Promise((resolve) => setTimeout(resolve, delay))
    }
  }

  throw lastError
}

// ─── ERROR LOGGING ────────────────────────────────────────────────────────────

const IS_DEV = isDev()

/**
 * Log error with context for debugging.
 * In development mode, logs full details to console.
 * In production, logs to a remote logging service (if configured).
 */
export function logError(error: NarsError, additionalContext?: ErrorContext): void {
  const fullContext = { ...error.context, ...additionalContext }

  /* eslint-disable no-console */
  if (IS_DEV) {
    console.group(`[NarsError] ${error.code}`)
    console.error("Message:", error.message)
    console.error("Context:", fullContext)
    if (error.cause) {
      console.error("Cause:", error.cause)
    }
    console.error("Stack:", error.stack)
    console.groupEnd()
  } else {
    // Production: send to backend logging endpoint
    captureError(error, additionalContext)
  }
  /* eslint-enable no-console */
}

// ─── ERROR HELPERS ────────────────────────────────────────────────────────────

/** i18n keys for user-facing messages, keyed by ErrorCode. */
const USER_MESSAGE_KEYS: Record<ErrorCode, string> = {
  [ErrorCode.NETWORK]: "err_network",
  [ErrorCode.VALIDATION]: "err_validation",
  [ErrorCode.AUTH]: "err_auth",
  [ErrorCode.NOT_FOUND]: "err_not_found",
  [ErrorCode.SERVER]: "err_server",
  [ErrorCode.TIMEOUT]: "err_timeout",
  [ErrorCode.PERMISSION]: "err_permission",
  [ErrorCode.CONFLICT]: "err_conflict",
  [ErrorCode.UNKNOWN]: "err_unknown",
}

/**
 * Resolve a user-facing i18n key for any thrown value. Returns a generic key for
 * non-NarsError values. Callers translate it via `t()`. Raw server bodies must
 * never be shown to end users — use `getErrorMessage`/`getTechnicalDetails` only
 * for logging.
 */
export function getUserMessageKey(err: unknown): string {
  if (isNarsError(err)) {
    return USER_MESSAGE_KEYS[err.code] ?? ErrorCode.UNKNOWN
  }
  return USER_MESSAGE_KEYS[ErrorCode.UNKNOWN]
}

/** Safely extract error message from any thrown value. Prevents `(err as Error).message` casts. */
export function getErrorMessage(err: unknown, fallback = "Unknown error"): string {
  if (err instanceof Error) return err.message
  if (typeof err === "string") return err
  try {
    return JSON.stringify(err) || fallback
  } catch {
    return fallback
  }
}

// ─── ERROR TYPE GUARDS ────────────────────────────────────────────────────────

export function isNarsError(error: unknown): error is NarsError {
  return error instanceof NarsError
}

export function isNetworkError(error: unknown): error is NarsError {
  return isNarsError(error) && error.code === ErrorCode.NETWORK
}

export function isValidationError(error: unknown): error is NarsError {
  return isNarsError(error) && error.code === ErrorCode.VALIDATION
}

export function isAuthError(error: unknown): error is NarsError {
  return isNarsError(error) && error.code === ErrorCode.AUTH
}
