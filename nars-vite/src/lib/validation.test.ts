// ─── VALIDATION TESTS ─────────────────────────────────────────────────────────
// Tests for validation.ts functions.

import { describe, it, expect, vi, beforeEach } from "vitest"
import {
  validateRoad,
  validateDistrict,
  checkDistrictCoverage,
  checkMainUrbanExists,
} from "./validation"

// Mock the api module
vi.mock("../api", () => ({
  apiFetch: vi.fn(),
}))

import { apiFetch } from "../api"

describe("validateRoad", () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it("rejects road shorter than minimum length", async () => {
    // Two points very close together (< 10m)
    const shortRoad = [
      { lat: 36.753, lng: 3.058 },
      { lat: 36.7531, lng: 3.0581 },
    ]

    // Mock apiFetch to return a rejection for short roads
    vi.mocked(apiFetch).mockResolvedValue({
      ok: false,
      status: 422,
      text: vi.fn().mockResolvedValue("Road too short"),
    } as any)

    const result = await validateRoad(shortRoad)

    // The client-side check should catch this first, but if turf fails,
    // the API will be called and should also reject
    expect(result.valid).toBe(false)
  })

  it("accepts road longer than minimum length", async () => {
    // Road ~500m long
    const longRoad = [
      { lat: 36.753, lng: 3.058 },
      { lat: 36.757, lng: 3.062 },
    ]

    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      json: vi.fn().mockResolvedValue({ valid: true, error: null }),
    } as any)

    const result = await validateRoad(longRoad)

    expect(result.valid).toBe(true)
    expect(result.error).toBeNull()
  })

  it("handles API failure gracefully", async () => {
    const road = [
      { lat: 36.753, lng: 3.058 },
      { lat: 36.757, lng: 3.062 },
    ]

    vi.mocked(apiFetch).mockRejectedValue(new TypeError("Failed to fetch"))

    const result = await validateRoad(road)

    expect(result.valid).toBe(false)
    expect(result.error).toContain("Cannot reach")
  })
})

describe("validateDistrict", () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it("closes open polygon rings", async () => {
    const openRing = [
      { lat: 36.753, lng: 3.058 },
      { lat: 36.754, lng: 3.059 },
      { lat: 36.755, lng: 3.058 },
    ]

    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      json: vi.fn().mockResolvedValue({ valid: true, error: null }),
    } as any)

    await validateDistrict(openRing, "district")

    const call = vi.mocked(apiFetch).mock.calls[0]
    const body = JSON.parse(call[1]?.body as string)

    // Should have 4 points (closed ring)
    expect(body.coordinates).toHaveLength(4)
    expect(body.coordinates[0]).toEqual(body.coordinates[3])
  })

  it("keeps already closed rings unchanged", async () => {
    const closedRing = [
      { lat: 36.753, lng: 3.058 },
      { lat: 36.754, lng: 3.059 },
      { lat: 36.755, lng: 3.058 },
      { lat: 36.753, lng: 3.058 },
    ]

    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      json: vi.fn().mockResolvedValue({ valid: true, error: null }),
    } as any)

    await validateDistrict(closedRing, "district")

    const call = vi.mocked(apiFetch).mock.calls[0]
    const body = JSON.parse(call[1]?.body as string)

    expect(body.coordinates).toHaveLength(4)
  })

  it("handles API failure gracefully", async () => {
    const district = [
      { lat: 36.753, lng: 3.058 },
      { lat: 36.754, lng: 3.059 },
      { lat: 36.755, lng: 3.058 },
      { lat: 36.753, lng: 3.058 },
    ]

    vi.mocked(apiFetch).mockRejectedValue(new Error("Network error"))

    const result = await validateDistrict(district)

    expect(result.valid).toBe(false)
    expect(result.error).toContain("Cannot reach")
  })
})

describe("checkDistrictCoverage", () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it("returns coverage status from API", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      json: vi.fn().mockResolvedValue({ covered: true, message: "All covered" }),
    } as any)

    const result = await checkDistrictCoverage()

    expect(result.covered).toBe(true)
    expect(result.message).toBe("All covered")
  })

  it("handles API failure gracefully", async () => {
    vi.mocked(apiFetch).mockRejectedValue(new Error("Network error"))

    const result = await checkDistrictCoverage()

    expect(result.covered).toBe(false)
    expect(result.message).toBeTruthy()
  })
})

describe("checkMainUrbanExists", () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it("returns true when main urban area exists", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      json: vi.fn().mockResolvedValue({ exists: true }),
    } as any)

    const result = await checkMainUrbanExists()

    expect(result).toBe(true)
  })

  it("returns false when main urban area does not exist", async () => {
    vi.mocked(apiFetch).mockResolvedValue({
      ok: true,
      json: vi.fn().mockResolvedValue({ exists: false }),
    } as any)

    const result = await checkMainUrbanExists()

    expect(result).toBe(false)
  })

  it("returns false on API failure", async () => {
    vi.mocked(apiFetch).mockRejectedValue(new Error("Network error"))

    const result = await checkMainUrbanExists()

    expect(result).toBe(false)
  })
})
