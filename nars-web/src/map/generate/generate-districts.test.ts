import { describe, it, expect, vi, beforeEach } from "vitest"
import { setActivePinia, createPinia } from "pinia"
import { ErrorCode } from "../../lib/errors"
import type { GenerateDistrictsResponse } from "../../api/generation"

const { mockGenerateDistricts, mockShowToast, mockGetUserMessageKey, mockDebugError } = vi.hoisted(
  () => ({
    mockGenerateDistricts: vi.fn(),
    mockShowToast: vi.fn(),
    mockGetUserMessageKey: vi.fn(() => "err_unknown"),
    mockDebugError: vi.fn(),
  }),
)

vi.mock("../../api/generation", () => ({ generateDistricts: mockGenerateDistricts }))
vi.mock("../../i18n", () => ({ t: (key: string) => key }))
vi.mock("../../lib/toast", () => ({ showToast: mockShowToast }))
vi.mock("../../lib/errors", async () => {
  const actual = await vi.importActual<typeof import("../../lib/errors")>("../../lib/errors")
  return { ...actual, getUserMessageKey: mockGetUserMessageKey }
})
vi.mock("../../utils/debug", () => ({
  debugError: mockDebugError,
  debugWarn: vi.fn(),
  debugLog: vi.fn(),
}))

let useAppStore: any
let useGenerationStore: any
let generateDistrictsFromUrbanAreas: any

const COMMUNE_ID = 42

function summary(overrides: Partial<GenerateDistrictsResponse> = {}): GenerateDistrictsResponse {
  return {
    districts: [
      { draftId: "d1", areaM2: 1200, lat: 36.7, lng: 2.9 },
      { draftId: "d2", areaM2: 900, lat: 36.71, lng: 2.91 },
    ],
    absorbedSlivers: 1,
    primaryRoadCount: 2,
    urbanAreaCount: 1,
    ...overrides,
  }
}

beforeEach(async () => {
  vi.clearAllMocks()
  vi.resetModules()
  setActivePinia(createPinia())

  const appMod = await import("../../stores/appStore")
  useAppStore = appMod.useAppStore
  useAppStore().setUser({
    id: 1,
    role: "commune_user",
    commune: { id: COMMUNE_ID, name_fr: "Alger", name_ar: "", latitude: null, longitude: null },
  } as any)

  const generationMod = await import("../../stores/generationStore")
  useGenerationStore = generationMod.useGenerationStore

  const mod = await import("./generate-districts")
  generateDistrictsFromUrbanAreas = mod.generateDistrictsFromUrbanAreas
})

describe("generateDistrictsFromUrbanAreas", () => {
  it("posts the signed-in commune and reports the drafts", async () => {
    mockGenerateDistricts.mockResolvedValue(summary())

    const result = await generateDistrictsFromUrbanAreas()

    expect(mockGenerateDistricts).toHaveBeenCalledWith(COMMUNE_ID)
    expect(result).toEqual({ created: 2, absorbedSlivers: 1 })
    // t() is mocked to identity here, so the toast sees the raw key.
    expect(mockShowToast).toHaveBeenCalledWith("gen_districts_done", "success")
  })

  it("releases the generation lock when it finishes", async () => {
    mockGenerateDistricts.mockResolvedValue(summary())

    await generateDistrictsFromUrbanAreas()

    // complete() leaves the bar at 100% until the settle timer hides it.
    expect(useGenerationStore().active).toBe(true)
    expect(useGenerationStore().progress).toBe(100)
  })

  it("reports an informational toast when nothing could be cut", async () => {
    mockGenerateDistricts.mockResolvedValue(
      summary({ districts: [], absorbedSlivers: 0, primaryRoadCount: 0 }),
    )

    const result = await generateDistrictsFromUrbanAreas()

    expect(result).toEqual({ created: 0, absorbedSlivers: 0 })
    expect(mockShowToast).toHaveBeenCalledWith("gen_districts_none", "info")
  })

  it("surfaces the missing-input case as a localized message, not raw API text", async () => {
    // The API rejects with 400 + an English ProblemDetails detail when the
    // commune has no urban area / no boulevard or avenue to cut along.
    const { createServerError } = await import("../../lib/errors")
    mockGenerateDistricts.mockRejectedValue(
      createServerError("Commune 42 has no boulevard or avenue roads.", { status: 400 }),
    )

    const result = await generateDistrictsFromUrbanAreas()

    expect(result).toBeNull()
    expect(mockShowToast).toHaveBeenCalledWith("gen_districts_no_input", "info")
    // The raw server prose must not leak into the UI.
    expect(mockShowToast).not.toHaveBeenCalledWith(
      expect.stringContaining("no boulevard or avenue"),
      expect.anything(),
    )
    expect(mockGetUserMessageKey).not.toHaveBeenCalled()
    expect(mockDebugError).toHaveBeenCalled()
    expect(useGenerationStore().active).toBe(false)
  })

  it("falls back to a generic error toast for other failures", async () => {
    const { createServerError } = await import("../../lib/errors")
    mockGenerateDistricts.mockRejectedValue(
      createServerError("boom", { status: 500 as never } as never),
    )

    const result = await generateDistrictsFromUrbanAreas()

    expect(result).toBeNull()
    expect(mockShowToast).toHaveBeenCalledWith("gen_districts_failed", "error")
    expect(useGenerationStore().active).toBe(false)
  })

  it("aborts without calling the API when the account has no commune", async () => {
    useAppStore().setUser({ id: 1, role: "commune_user", commune: null } as any)

    const result = await generateDistrictsFromUrbanAreas()

    expect(result).toBeNull()
    expect(mockGenerateDistricts).not.toHaveBeenCalled()
    expect(mockShowToast).toHaveBeenCalledWith("gen_roads_no_commune", "error")
  })

  it("refuses to start while another generation is in flight", async () => {
    useGenerationStore().begin("gen_roads_stage_tiles")

    const result = await generateDistrictsFromUrbanAreas()

    expect(result).toBeNull()
    expect(mockGenerateDistricts).not.toHaveBeenCalled()
    expect(mockShowToast).toHaveBeenCalledWith("gen_districts_in_progress", "warning")
  })

  it("does not swallow auth errors", async () => {
    const { createAuthError } = await import("../../lib/errors")
    mockGetUserMessageKey.mockReturnValueOnce("err_auth")
    mockGenerateDistricts.mockRejectedValue(
      createAuthError("forbidden", { status: 403, code: ErrorCode.AUTH }),
    )

    const result = await generateDistrictsFromUrbanAreas()

    expect(result).toBeNull()
    // 403 is not the missing-input case, so it goes through the generic path.
    expect(mockShowToast).toHaveBeenCalledWith("gen_districts_failed", "error")
  })
})
