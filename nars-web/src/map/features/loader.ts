// ─── LOADER (ORCHESTRATOR) ────────────────────────────────────────────────────
// Re-exports loadFromDatabase and loadUserAndCommune.

import { apiFetch } from "../../api"
import { useAppStore } from "../../stores/appStore"
import { debugError } from "../../utils/debug"

export { loadFromDatabase } from "./loader-db"

export async function loadUserAndCommune(): Promise<void> {
  try {
    const user = await apiFetch("/api/current_user").then((r) => r.json())
    useAppStore().setUser(user)
  } catch (err) {
    debugError("Commune nav error:", err)
  }
}
