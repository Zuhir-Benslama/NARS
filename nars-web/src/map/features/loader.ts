// ─── LOADER (ORCHESTRATOR) ────────────────────────────────────────────────────
// Re-exports loadFromDatabase and loadUserAndCommune.

import { apiFetch } from "../../api"
import { useAppStore } from "../../stores/appStore"
import { debugError } from "../../utils/debug"

export { loadFromDatabase } from "./loader-db"

export async function loadUserAndCommune(): Promise<void> {
  try {
    const user = await (await apiFetch("/api/current_user")).json()
    useAppStore().setUser(user)
  } catch (err) {
    debugError("Commune nav error:", err)
    // Boot without a user means the app runs as an anonymous commune_user with
    // no boundary and no data — surface it instead of silently degrading.
    useAppStore().setLoadError(true)
  }
}
