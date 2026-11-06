// ─── FEATURE VALIDATION TESTS ─────────────────────────────────────────────────
// Tests for composables/useFeatureValidation.ts.

import { describe, it, expect, vi, beforeEach } from "vitest"
import type { ModalState } from "../types"

beforeEach(() => {
  localStorage.clear()
})

async function loadModule() {
  return vi.importActual<typeof import("./useFeatureValidation")>("./useFeatureValidation")
}

function phaseIndexOf(PHASES: any[], key: string): number {
  const idx = PHASES.findIndex((p: any) => p.key === key)
  if (idx === -1) throw new Error(`unknown phase: ${key}`)
  return idx
}

function storeFor(PHASES: any[], key: string, overrides: Partial<ModalState> = {}) {
  return {
    phaseIndex: phaseIndexOf(PHASES, key),
    label: "Rue Exemple",
    decisionNumber: "",
    decisionDate: "",
    areaTypeKey: "central_urban",
    districtTypeKey: "district",
    roadTypeKey: "street",
    spaceTypeKey: "garden",
    sectorKey: "banking_postal",
    buildingTypeKey: "bank",
    radius: null,
    ...overrides,
  } as ModalState & { phaseIndex: number }
}

describe("validate — decision fields", () => {
  it("does not require a decision number or date for roads", async () => {
    const { PHASES } = await vi.importActual<typeof import("../phases")>("../phases")
    const mod = await loadModule()
    const { validate } = mod.useFeatureValidation(storeFor(PHASES, "roads"))
    const errors = validate()
    expect(errors.decisionNumber).toBeUndefined()
    expect(errors.decisionDate).toBeUndefined()
    expect(errors).toEqual({})
  })

  it("still requires a decision number and date for other phases", async () => {
    const { PHASES } = await vi.importActual<typeof import("../phases")>("../phases")
    const mod = await loadModule()
    for (const key of ["areas", "districts", "publicBuildings", "publicSpaces", "namingPanels"]) {
      const { validate } = mod.useFeatureValidation(storeFor(PHASES, key))
      const errors = validate()
      expect(errors.decisionNumber, key).toBe("Required")
      expect(errors.decisionDate, key).toBe("Required")
    }
  })

  it("still requires a label for roads", async () => {
    const { PHASES } = await vi.importActual<typeof import("../phases")>("../phases")
    const mod = await loadModule()
    const { validate } = mod.useFeatureValidation(storeFor(PHASES, "roads", { label: "   " }))
    expect(validate().label).toBe("Required")
  })

  it("does not require decision fields for city centers", async () => {
    const { PHASES } = await vi.importActual<typeof import("../phases")>("../phases")
    const mod = await loadModule()
    const { validate } = mod.useFeatureValidation(storeFor(PHASES, "cityCenter", { radius: 500 }))
    const errors = validate()
    expect(errors.decisionNumber).toBeUndefined()
    expect(errors.decisionDate).toBeUndefined()
  })
})

describe("buildModalResult — roads", () => {
  it("emits blank decision fields rather than dropping them", async () => {
    const { PHASES } = await vi.importActual<typeof import("../phases")>("../phases")
    const mod = await loadModule()
    const { buildModalResult } = mod.useFeatureValidation(storeFor(PHASES, "roads"))
    const result = buildModalResult("Commune")
    expect(result).toEqual({
      type: "roads",
      label: "Street Rue Exemple",
      decisionNumber: "",
      decisionDate: "",
      roadTypeKey: "street",
    })
  })

  it("preserves a decision number the user did supply", async () => {
    const { PHASES } = await vi.importActual<typeof import("../phases")>("../phases")
    const mod = await loadModule()
    const { buildModalResult } = mod.useFeatureValidation(
      storeFor(PHASES, "roads", { decisionNumber: " 2026/014 ", decisionDate: "2026-02-01" }),
    )
    const result = buildModalResult("Commune")
    expect(result).toMatchObject({ decisionNumber: "2026/014", decisionDate: "2026-02-01" })
  })
})
