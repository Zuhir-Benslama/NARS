// ─── FEATURE VALIDATION TESTS ─────────────────────────────────────────────────
// Tests for composables/useFeatureValidation.ts.
//
// Regression guard: AI-generated roads are materialized with blank decision
// fields, and requiring them made every generated road unsaveable via the
// "Edit info" modal (FeatureModal.onSave aborts on any validation error).

import { describe, it, expect } from "vitest"
import { useFeatureValidation } from "./useFeatureValidation"
import { PHASES } from "../phases"
import type { ModalState } from "../types"

function phaseIndexOf(key: string): number {
  const idx = PHASES.findIndex((p) => p.key === key)
  if (idx === -1) throw new Error(`unknown phase: ${key}`)
  return idx
}

function storeFor(key: string, overrides: Partial<ModalState> = {}) {
  return {
    phaseIndex: phaseIndexOf(key),
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
  it("does not require a decision number or date for roads", () => {
    const { validate } = useFeatureValidation(storeFor("roads"))

    // A generated road arrives with no decision metadata at all.
    const errors = validate()

    expect(errors.decisionNumber).toBeUndefined()
    expect(errors.decisionDate).toBeUndefined()
    expect(errors).toEqual({})
  })

  it("still requires a decision number and date for other phases", () => {
    for (const key of ["areas", "districts", "publicBuildings", "publicSpaces", "namingPanels"]) {
      const { validate } = useFeatureValidation(storeFor(key))

      const errors = validate()

      expect(errors.decisionNumber, key).toBe("Required")
      expect(errors.decisionDate, key).toBe("Required")
    }
  })

  it("still requires a label for roads", () => {
    const { validate } = useFeatureValidation(storeFor("roads", { label: "   " }))

    expect(validate().label).toBe("Required")
  })

  it("does not require decision fields for city centers", () => {
    const { validate } = useFeatureValidation(storeFor("cityCenter", { radius: 500 }))

    const errors = validate()

    expect(errors.decisionNumber).toBeUndefined()
    expect(errors.decisionDate).toBeUndefined()
  })
})

describe("buildModalResult — roads", () => {
  it("emits blank decision fields rather than dropping them", () => {
    const { buildModalResult } = useFeatureValidation(storeFor("roads"))

    const result = buildModalResult("Commune")

    expect(result).toEqual({
      type: "roads",
      label: "Rue Exemple",
      decisionNumber: "",
      decisionDate: "",
      roadTypeKey: "street",
    })
  })

  it("preserves a decision number the user did supply", () => {
    const { buildModalResult } = useFeatureValidation(
      storeFor("roads", { decisionNumber: " 2026/014 ", decisionDate: "2026-02-01" }),
    )

    const result = buildModalResult("Commune")

    expect(result).toMatchObject({ decisionNumber: "2026/014", decisionDate: "2026-02-01" })
  })
})
