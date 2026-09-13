import { beforeEach, describe, expect, it, vi } from "vitest"
import { createPinia, setActivePinia } from "pinia"
import type { AiDraftFeatureDto } from "../../api/drafts"

const mocks = vi.hoisted(() => {
  const setData = vi.fn()
  return {
    setData,
    acceptDraft: vi.fn(),
    rejectDraft: vi.fn(),
    deleteDraft: vi.fn(),
    showToast: vi.fn(),
    showConfirm: vi.fn(),
    debugError: vi.fn(),
  }
})

vi.mock("../../api/drafts", () => ({
  acceptDraft: mocks.acceptDraft,
  rejectDraft: mocks.rejectDraft,
  deleteDraft: mocks.deleteDraft,
}))

vi.mock("../../lib/toast", () => ({
  showToast: mocks.showToast,
  showConfirm: mocks.showConfirm,
}))

vi.mock("../../utils/debug", () => ({
  debugError: mocks.debugError,
  debugWarn: vi.fn(),
  debugLog: vi.fn(),
}))

vi.mock("../core/state", () => ({
  tryGetCtx: () => ({ draftsSource: { setData: mocks.setData } }),
  getCtx: () => ({}),
}))

import { useDraftsStore } from "../../stores/draftsStore"
import { reviewDraft } from "./review-actions"

const DRAFT: AiDraftFeatureDto = {
  id: "d1",
  featureType: "road",
  geometryGeoJson: '{"type":"LineString","coordinates":[[36.72,2.96],[36.73,2.97]]}',
  confidence: 0.9,
  status: "pending",
  createdAt: "2026-01-01T00:00:00Z",
}

describe("reviewDraft", () => {
  beforeEach(() => {
    vi.clearAllMocks()
    setActivePinia(createPinia())
    useDraftsStore().drafts = [DRAFT]
  })

  it("accepts without confirmation, calls acceptDraft and removes the draft", async () => {
    mocks.acceptDraft.mockResolvedValue(undefined)

    const result = await reviewDraft("d1", "accept")

    expect(mocks.showConfirm).not.toHaveBeenCalled()
    expect(mocks.acceptDraft).toHaveBeenCalledWith("d1")
    expect(useDraftsStore().drafts).toEqual([])
    expect(mocks.showToast).toHaveBeenCalledWith("draft_accepted", "success")
    expect(result).toBe(true)
  })

  it("rejects after the user confirms", async () => {
    mocks.showConfirm.mockResolvedValue(true)
    mocks.rejectDraft.mockResolvedValue(undefined)

    const result = await reviewDraft("d1", "reject")

    expect(mocks.showConfirm).toHaveBeenCalledWith("draft_confirm_reject")
    expect(mocks.rejectDraft).toHaveBeenCalledWith("d1")
    expect(useDraftsStore().drafts).toEqual([])
    expect(mocks.showToast).toHaveBeenCalledWith("draft_rejected", "success")
    expect(result).toBe(true)
  })

  it("deletes after the user confirms", async () => {
    mocks.showConfirm.mockResolvedValue(true)
    mocks.deleteDraft.mockResolvedValue(undefined)

    const result = await reviewDraft("d1", "delete")

    expect(mocks.showConfirm).toHaveBeenCalledWith("draft_confirm_delete")
    expect(mocks.deleteDraft).toHaveBeenCalledWith("d1")
    expect(useDraftsStore().drafts).toEqual([])
    expect(mocks.showToast).toHaveBeenCalledWith("draft_deleted", "success")
    expect(result).toBe(true)
  })

  it("aborts reject when the user declines the confirmation", async () => {
    mocks.showConfirm.mockResolvedValue(false)

    const result = await reviewDraft("d1", "reject")

    expect(mocks.rejectDraft).not.toHaveBeenCalled()
    expect(useDraftsStore().drafts).toHaveLength(1)
    expect(mocks.showToast).not.toHaveBeenCalledWith("draft_rejected", "success")
    expect(result).toBe(false)
  })

  it("keeps the draft and reports failure when the API call rejects", async () => {
    mocks.showConfirm.mockResolvedValue(true)
    mocks.rejectDraft.mockRejectedValue(new Error("boom"))

    const result = await reviewDraft("d1", "reject")

    expect(useDraftsStore().drafts).toHaveLength(1)
    expect(mocks.debugError).toHaveBeenCalled()
    expect(mocks.showToast).toHaveBeenCalledWith("draft_action_failed", "error")
    expect(result).toBe(false)
  })
})
