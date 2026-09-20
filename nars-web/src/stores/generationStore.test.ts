import { describe, it, expect, beforeEach, afterEach, vi } from "vitest"
import { setActivePinia, createPinia } from "pinia"

import { useGenerationStore } from "./generationStore"
import { GEN_CONFIG } from "../config"

describe("useGenerationStore", () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it("begins in an idle state", () => {
    const store = useGenerationStore()
    expect(store.active).toBe(false)
    expect(store.progress).toBe(0)
    expect(store.stage).toBe("")
  })

  it("begin activates with the given stage and 0%", () => {
    const store = useGenerationStore()
    expect(store.begin("gen_roads_stage_tiles")).toBe(true)
    expect(store.active).toBe(true)
    expect(store.progress).toBe(0)
    expect(store.stage).toBe("gen_roads_stage_tiles")
  })

  it("does not re-begin while already active", () => {
    const store = useGenerationStore()
    store.begin("gen_roads_stage_tiles")
    expect(store.begin("gen_roads_stage_tiles")).toBe(false)
    expect(store.progress).toBe(0)
  })

  it("setProgress updates the percentage and optional stage", () => {
    const store = useGenerationStore()
    store.begin("gen_roads_stage_tiles")
    store.setProgress(35, "gen_roads_stage_detect")
    expect(store.progress).toBe(35)
    expect(store.stage).toBe("gen_roads_stage_detect")
  })

  it("clamps setProgress to 0..100", () => {
    const store = useGenerationStore()
    store.begin("gen_roads_stage_tiles")
    store.setProgress(-10)
    expect(store.progress).toBe(0)
    store.setProgress(150)
    expect(store.progress).toBe(100)
  })

  it("ignores setProgress while idle", () => {
    const store = useGenerationStore()
    store.setProgress(50, "gen_roads_stage_detect")
    expect(store.progress).toBe(0)
    expect(store.stage).toBe("")
  })

  it("complete fills to 100% then hides after the settle delay", () => {
    vi.useFakeTimers()
    const store = useGenerationStore()
    store.begin("gen_roads_stage_tiles")
    store.complete()
    expect(store.progress).toBe(100)
    expect(store.active).toBe(true)

    vi.advanceTimersByTime(GEN_CONFIG.completeSettleMs - 1)
    expect(store.active).toBe(true)

    vi.advanceTimersByTime(1)
    expect(store.active).toBe(false)
    expect(store.progress).toBe(0)
    expect(store.stage).toBe("")
  })

  it("abort hides immediately without filling the bar", () => {
    const store = useGenerationStore()
    store.begin("gen_roads_stage_tiles")
    store.setProgress(35)
    store.abort()
    expect(store.active).toBe(false)
    expect(store.progress).toBe(0)
  })

  it("complete is a no-op while idle", () => {
    const store = useGenerationStore()
    store.complete()
    expect(store.active).toBe(false)
  })
})
