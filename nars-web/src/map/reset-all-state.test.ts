import { describe, it, expect, vi, beforeEach } from "vitest"

const {
  mockResetDrawState,
  mockResetEditState,
  mockResetUndoStack,
  mockResetSnapState,
  mockResetDrawControl,
  mockResetBoundaryEvents,
  mockResetRotation,
  mockResetMapState,
} = vi.hoisted(() => ({
  mockResetDrawState: vi.fn(),
  mockResetEditState: vi.fn(),
  mockResetUndoStack: vi.fn(),
  mockResetSnapState: vi.fn(),
  mockResetDrawControl: vi.fn(),
  mockResetBoundaryEvents: vi.fn(),
  mockResetRotation: vi.fn(),
  mockResetMapState: vi.fn(),
}))

vi.mock("./draw/draw-state", () => ({ resetDrawState: mockResetDrawState }))
vi.mock("./edit/edit-state", () => ({ resetEditState: mockResetEditState }))
vi.mock("./undo", () => ({ resetUndoStack: mockResetUndoStack }))
vi.mock("./snapping/snapping", () => ({ resetSnapState: mockResetSnapState }))
vi.mock("./draw/draw-control", () => ({ resetDrawControl: mockResetDrawControl }))
vi.mock("./map-boundary", () => ({ resetBoundaryEvents: mockResetBoundaryEvents }))
vi.mock("./rotation", () => ({ resetRotation: mockResetRotation }))
vi.mock("./core/state", () => ({ resetMapState: mockResetMapState }))

describe("resetAllState", () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it("calls every reset function", async () => {
    const { resetAllState } = await import("./reset-all-state")
    resetAllState()

    expect(mockResetDrawState).toHaveBeenCalledOnce()
    expect(mockResetEditState).toHaveBeenCalledOnce()
    expect(mockResetUndoStack).toHaveBeenCalledOnce()
    expect(mockResetSnapState).toHaveBeenCalledOnce()
    expect(mockResetDrawControl).toHaveBeenCalledOnce()
    expect(mockResetBoundaryEvents).toHaveBeenCalledOnce()
    expect(mockResetRotation).toHaveBeenCalledOnce()
    expect(mockResetMapState).toHaveBeenCalledOnce()
  })
})
