import { NarsError, type ErrorContext } from "./errors"
import { getApiBaseUrl, isDev } from "../config"
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
      const res = await fetch(`${getApiBaseUrl()}/api/logs`, {
        method: "POST",
        credentials: "include",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ logs: entries }),
      })
      if (res.ok) break
    } catch (err) {
      // Silently ignore in production — don't create a feedback loop by logging the logger
      if (isDev()) debugWarn("[Logger] Failed to send log batch:", err)
    }
  }

  flushing = false
  if (batch.length > 0) flush()
}

if (typeof window !== "undefined") {
  window.addEventListener("beforeunload", () => {
    if (timer) clearTimeout(timer)
    if (batch.length > 0) {
      const body = JSON.stringify({ logs: batch })
      navigator.sendBeacon(`${getApiBaseUrl()}/api/logs`, body)
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
