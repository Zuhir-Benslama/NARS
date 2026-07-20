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

const batch: LogEntry[] = []
let timer: ReturnType<typeof setTimeout> | null = null
let flushing = false

function push(entry: LogEntry): void {
  batch.push(entry)
  if (batch.length >= BATCH_LIMIT) {
    flush()
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

  const entries = batch.splice(0, BATCH_LIMIT)
  flushing = true

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

  flushing = false
  if (batch.length > 0) setTimeout(flush, 0)
}

if (typeof window !== "undefined") {
  window.addEventListener("beforeunload", () => {
    if (timer) clearTimeout(timer)
    if (batch.length > 0) {
      const csrfToken = getCsrfToken()
      const qs = csrfToken ? `?csrf=${encodeURIComponent(csrfToken)}` : ""

      const blob = new Blob([JSON.stringify({ logs: batch })], { type: "application/json" })
      navigator.sendBeacon(`${getApiBaseUrl()}/api/logs${qs}`, blob)
    }
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

export function captureError(error: NarsError, additionalContext?: ErrorContext): void {
  const fullContext = { ...error.context, ...additionalContext }
  push({
    level: "error",
    code: error.code,
    message: error.message,
    context: Object.keys(fullContext).length > 0 ? JSON.stringify(fullContext) : null,
    url: fullContext.url ?? null,
    method: fullContext.method ?? null,
  })
}

export function flushLogs(): Promise<void> {
  if (batch.length === 0) return Promise.resolve()
  return flush()
}
