import { apiFetch } from "../../api"
import { logError, createServerError, getErrorMessage, getUserMessageKey } from "../../lib/errors"
import { debugError } from "../../utils/debug"
import { t } from "../../i18n"
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
    const technical = getErrorMessage(err)
    logError(createServerError(technical, { action: "saveToDatabase" }, err))
    debugError("[SAVE] Database save failed:", {
      message: technical,
      stack: err instanceof Error ? err.stack : undefined,
    })
    return { ok: false, error: t(getUserMessageKey(err)) }
  }
}
