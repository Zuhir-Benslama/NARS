// ─── DRAW CONTROL ─────────────────────────────────────────────────────────────
// Uses Geoman's native enableDraw() for the current phase's geometry type.
// Mirrors the reference implementation in nars-web/src/map/draw-control.ts

import { ctx } from "../core/state"
import { debugLog, debugError } from "../../utils/debug"
import { ensureGeomanDrawEdgesVisible } from "../edit/edit-state"
import { DRAW_CONFIG } from "../../config"
import { useDrawStore } from "../../stores/drawStore"
import type { DrawModeName } from "@geoman-io/maplibre-geoman-free"

type PhaseConfig = { key: string; drawType: string; color: string }

const DRAW_TYPE_MAP: Record<string, DrawModeName> = {
  polygon: "polygon",
  polyline: "line",
  marker: "marker",
  circle: "circle",
}

export function resetDrawControl(): void {
  const store = useDrawStore()
  store.lastPhaseKey = null
  store.modeSwitchToken = 0
}

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

export function buildDrawControl(phase: PhaseConfig): void {
  const gm = ctx.geoman
  if (!gm) return

  const store = useDrawStore()
  const shapeName = DRAW_TYPE_MAP[phase.drawType]
  if (!shapeName) return

  debugLog("[DRAW CONTROL] Phase:", phase.key, "| drawType:", phase.drawType, "| shape:", shapeName)

  const activeModes = gm.getActiveDrawModes?.() || []
  if (store.lastPhaseKey === phase.key && activeModes.length > 0 && activeModes[0] === shapeName) {
    debugLog("[DRAW CONTROL] Already in correct draw mode:", shapeName)
    return
  }

  store.lastPhaseKey = phase.key
  const token = ++store.modeSwitchToken

  void (async () => {
    if (activeModes.length > 0) {
      debugLog("[DRAW CONTROL] Disabling current draw mode:", activeModes)
      try {
        await gm.disableDraw()
      } catch (err) {
        debugError("[DRAW CONTROL] Failed to disable draw mode:", err)
      }
    }

    await delay(DRAW_CONFIG.modeSwitchSettleMs)

    if (token !== store.modeSwitchToken || store.lastPhaseKey !== phase.key) return

    const currentGm = ctx.geoman
    if (!currentGm) return

    debugLog("[DRAW CONTROL] Enabling draw mode:", shapeName)
    currentGm
      .enableDraw(shapeName)
      .then(() => {
        debugLog("[DRAW CONTROL] Enabled draw mode:", shapeName)
        if (shapeName === "polygon" || shapeName === "line") {
          ensureGeomanDrawEdgesVisible()
          let retries = 0
          const poll = setInterval(() => {
            if (++retries > DRAW_CONFIG.edgeRetryMax) {
              clearInterval(poll)
              return
            }
            ensureGeomanDrawEdgesVisible()
          }, DRAW_CONFIG.edgeRetryIntervalMs)
          setTimeout(() => clearInterval(poll), DRAW_CONFIG.edgeRetryTimeoutMs)
        }
      })
      .catch((err) => debugError("[DRAW CONTROL] Failed to enable draw mode:", shapeName, err))
  })()
}
