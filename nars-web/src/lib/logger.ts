import { NarsError, type ErrorContext } from "./errors"
import { getApiBaseUrl, isDev } from "../config"
import { getCsrfToken } from "./csrf"
import { debugWarn } from "../utils/debug"

interface LogEntry {
  level: string
  code: string
  message: string
  context: string | null
  url: string | null
  method: string | null
}

const BATCH_LIMIT = 20
const FLUSH_INTERVAL_MS = 30_000
const MAX_RETRIES = 1
const REQUEST_TIMEOUT_MS = 5_000
// The Fetch spec rejects keepalive bodies over 64KB outright; stay safely
// under it and trim entries rather than losing the whole final batch.
const MAX_KEEPALIVE_BYTES = 60_000

const batch: LogEntry[] = []
let timer: ReturnType<typeof setTimeout> | null = null
let flushing = false

function push(entry: LogEntry): void {
  batch.push(entry)
  if (batch.length >= BATCH_LIMIT) {
    void flush()
  } else if (!timer) {
    timer = setTimeout(flush, FLUSH_INTERVAL_MS)
  }
}

async function flush(): Promise<void> {
  if (timer) {
    clearTimeout(timer)
    timer = null
  }
  if (batch.length === 0 || flushing) return

  // Take the entries up front: if every attempt fails we drop them rather
  // than re-sending the same slice forever while the batch grows unbounded.
  // Logging is best-effort telemetry — a down endpoint shouldn't leak memory
  // or spam itself with duplicate batches.
  const entries = batch.splice(0, BATCH_LIMIT)
  flushing = true

  try {
    for (let attempt = 0; attempt <= MAX_RETRIES; attempt++) {
      try {
        const controller = new AbortController()
        const timeoutId = setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS)

        const headers: Record<string, string> = { "Content-Type": "application/json" }
        const csrfToken = getCsrfToken()
        if (csrfToken) headers["X-CSRF-Token"] = csrfToken

        const res = await fetch(`${getApiBaseUrl()}/api/logs`, {
          method: "POST",
          credentials: "include",
          headers,
          body: JSON.stringify({ logs: entries }),
          signal: controller.signal,
        })
        clearTimeout(timeoutId)
        if (res.ok) break
      } catch (err) {
        // Silently ignore in production — don't create a feedback loop by logging the logger
        if (isDev()) debugWarn("[Logger] Failed to send log batch:", err)
      }
    }
  } finally {
    flushing = false
  }

  // Track the re-scheduled timer so push()/resetLoggerState()/beforeunload
  // don't end up with two overlapping flush timers.
  if (batch.length > 0) timer = setTimeout(flush, FLUSH_INTERVAL_MS)
}

if (typeof window !== "undefined") {
  // pagehide, not beforeunload: it also fires on mobile Safari and when the
  // tab enters the back/forward cache, where beforeunload is skipped.
  window.addEventListener("pagehide", () => {
    if (timer) clearTimeout(timer)
    timer = null
    if (batch.length === 0) return
    const pending = batch.splice(0)

    const headers: Record<string, string> = { "Content-Type": "application/json" }
    const csrfToken = getCsrfToken()
    if (csrfToken) headers["X-CSRF-Token"] = csrfToken

    // keepalive lets the request outlive the document AND carry headers.
    // sendBeacon could do neither: without X-CSRF-Token the server's
    // antiforgery middleware rejected every authenticated final batch with
    // 403, so pending logs were silently lost on every page close. Do NOT
    // pass the token as a query parameter — it leaks via Referer and logs.
    let body = JSON.stringify({ logs: pending })
    while (pending.length > 0 && body.length > MAX_KEEPALIVE_BYTES) {
      pending.pop()
      body = JSON.stringify({ logs: pending })
    }
    if (pending.length === 0) return

    void fetch(`${getApiBaseUrl()}/api/logs`, {
      method: "POST",
      credentials: "include",
      headers,
      body,
      keepalive: true,
    }).catch(() => {
      // Nothing left to do at unload — never log the logger here either.
    })
  })
}

if (import.meta.hot) {
  import.meta.hot.dispose(() => {
    if (timer) clearTimeout(timer)
    timer = null
    batch.length = 0
  })
}

export function resetLoggerState(): void {
  if (timer) {
    clearTimeout(timer)
    timer = null
  }
  batch.length = 0
  flushing = false
}

/**
 * Drop the query string before shipping a URL to the server: it can carry
 * user input (e.g. ?search=… terms) and the log pipeline's privacy posture
 * matches telemetry's, which already strips queries before export.
 */
function stripUrlQuery(url: string | null | undefined): string | null {
  if (!url) return null
  const queryStart = url.indexOf("?")
  return queryStart === -1 ? url : url.slice(0, queryStart)
}

export function captureError(error: NarsError, additionalContext?: ErrorContext): void {
  const fullContext = { ...error.context, ...additionalContext }
  // url/method travel in their own dedicated (sanitized) fields; keeping them
  // out of the free-form context blob prevents the raw query string from
  // sneaking back in through JSON.stringify.
  const restContext = { ...fullContext }
  delete restContext.url
  delete restContext.method
  push({
    level: "error",
    code: error.code,
    message: error.message,
    context: Object.keys(restContext).length > 0 ? JSON.stringify(restContext) : null,
    url: stripUrlQuery(fullContext.url),
    method: fullContext.method ?? null,
  })
}

export function flushLogs(): Promise<void> {
  if (batch.length === 0) return Promise.resolve()
  return flush()
}
