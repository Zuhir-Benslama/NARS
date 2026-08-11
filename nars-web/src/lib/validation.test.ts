// ─── VALIDATION TESTS ─────────────────────────────────────────────────────────
// Tests for validation.ts functions.

import { describe, it, expect, vi, beforeEach } from "vitest"
import { checkDistrictCoverage, checkMainUrbanExists } from "./validation"

// Mock the api module
vi.mock("../api", () => ({
  apiFetch: vi.fn(),
}))

import { apiFetch } from "../api"

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
