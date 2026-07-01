import { apiFetch } from "../../api"
import { logError, createServerError } from "../../lib/errors"
import { debugError } from "../../utils/debug"
import type { FeatureData, SaveResult } from "../../types"
import { toApiSaveShape } from "./feature-data"

export async function saveToDatabase(featureData: FeatureData): Promise<SaveResult> {
  try {
    const shape = toApiSaveShape(featureData)

    const response = await apiFetch("/api/features", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        type: shape.type,
        layer: shape.layer,
        label: featureData.label,
        data: featureData,
      }),
    })
    const data = (await response.json()) as { id: string }
    return { ok: true, data }
  } catch (err) {
    const message = err instanceof Error ? err.message : "Unknown error"
    const cause =
      err instanceof Error && "cause" in err
        ? String((err as Error & { cause?: unknown }).cause)
        : undefined
    logError(createServerError(message, { action: "saveToDatabase" }, err))
    debugError("[SAVE] Database save failed:", {
      message,
      context: cause,
      stack: err instanceof Error ? err.stack : undefined,
    })
    return { ok: false, error: message }
  }
}
