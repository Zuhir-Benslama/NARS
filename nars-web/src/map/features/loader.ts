// ─── LOADER (ORCHESTRATOR) ────────────────────────────────────────────────────
// Re-exports loadFromDatabase and loadUserAndCommune.

import { apiFetch } from "../../api"
import { useAppStore } from "../../stores/appStore"
import { displayCommuneBoundary } from "../rendering/geometry"
import { debugError } from "../../utils/debug"

export { loadFromDatabase } from "./loader-db"

export async function loadUserAndCommune(): Promise<void> {
  try {
    const user = await apiFetch("/api/current_user").then((r) => r.json())
    const appStore = useAppStore()
    appStore.setUser(user)
    appStore.municipalityName = user.commune?.name_fr ?? ""
    if (user.commune?.id) await displayCommuneBoundary(user.commune.id as number)
  } catch (err) {
    debugError("Commune nav error:", err)
  }
}
