import { describe, it, expect } from "vitest"
import { cityCenterRadiusError } from "./city-center"

describe("cityCenterRadiusError", () => {
  it("returns too_small for null/undefined/NaN radius", () => {
    expect(cityCenterRadiusError(null)).toBe("too_small")
    expect(cityCenterRadiusError(undefined)).toBe("too_small")
    expect(cityCenterRadiusError(Number.NaN)).toBe("too_small")
  })

  it("returns too_small below the minimum radius", () => {
    expect(cityCenterRadiusError(0)).toBe("too_small")
    expect(cityCenterRadiusError(4.9)).toBe("too_small")
    expect(cityCenterRadiusError(5)).toBeNull()
  })

  it("returns too_large above the maximum radius", () => {
    expect(cityCenterRadiusError(50_000.1)).toBe("too_large")
  })

  it("returns null for a valid radius", () => {
    expect(cityCenterRadiusError(10)).toBeNull()
    expect(cityCenterRadiusError(50_000)).toBeNull()
  })
})
