// ─── ROAD GENERATION PROGRESS ────────────────────────────────────────────────
// Tracks the generate-roads flow so the UI can show a progress bar from the
// moment generation starts until it settles. Stages map to i18n keys rendered
// next to the bar.

import { defineStore } from "pinia"
import { GEN_CONFIG } from "../config"

export const useGenerationStore = defineStore("generation", {
  state: () => ({
    active: false,
    progress: 0,
    stage: "",
    settleTimer: null as ReturnType<typeof setTimeout> | null,
  }),

  actions: {
    /**
     * Activates the progress bar. Returns false (without starting) when a
     * generation is already in flight so the flow can opt out of duplicating.
     */
    begin(stage: string): boolean {
      if (this.active) return false
      this.active = true
      this.progress = 0
      this.stage = stage
      return true
    },

    /** Advances the bar to a percentage (0-100), updating the stage label in place. */
    setProgress(progress: number, stage?: string): void {
      if (!this.active) return
      this.progress = Math.max(0, Math.min(100, progress))
      if (stage) this.stage = stage
    },

    /** Fills the bar to 100%, then hides it after a short settle so the UX reads as "done". */
    complete(): void {
      if (!this.active) return
      this.progress = 100
      this.clearSettleTimer()
      this.settleTimer = setTimeout(() => {
        this.reset()
      }, GEN_CONFIG.completeSettleMs)
    },

    /** Hides the bar immediately (used on failure). */
    abort(): void {
      this.reset()
    },

    reset(): void {
      this.clearSettleTimer()
      this.active = false
      this.progress = 0
      this.stage = ""
    },

    clearSettleTimer(): void {
      if (this.settleTimer) {
        clearTimeout(this.settleTimer)
        this.settleTimer = null
      }
    },
  },
})

export function resetGenerationStore(): void {
  useGenerationStore().reset()
}
