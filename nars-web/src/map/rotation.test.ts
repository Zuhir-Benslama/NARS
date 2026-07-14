import { describe, it, expect, vi, beforeEach } from "vitest"

const { mockEaseTo, mockGetContainer } = vi.hoisted(() => ({
  mockEaseTo: vi.fn(),
  mockGetContainer: vi.fn(() => document.createElement("div")),
}))

vi.mock("./core/state", () => ({
  getCtx: () => ({
    map: {
      easeTo: mockEaseTo,
      getContainer: mockGetContainer,
    },
  }),
}))

vi.mock("../i18n", () => ({
  t: (key: string) => key,
}))

let resetRotation: () => void
let setBearing: (deg: number) => void
let initRotationControls: () => void

async function loadModule() {
  const mod = await import("./rotation")
  resetRotation = mod.resetRotation
  setBearing = mod.setBearing
  initRotationControls = mod.initRotationControls
}

describe("rotation", () => {
  beforeEach(async () => {
    mockEaseTo.mockReset()
    mockGetContainer.mockClear()
    await loadModule()
    resetRotation()
  })

  describe("setBearing", () => {
    it("calls map.easeTo with the given bearing", () => {
      setBearing(45)
      expect(mockEaseTo).toHaveBeenCalledWith({
        bearing: 45,
        duration: 300,
      })
    })

    it("wraps bearing beyond 360", () => {
      setBearing(370)
      expect(mockEaseTo).toHaveBeenCalledWith(expect.objectContaining({ bearing: 10 }))
    })

    it("wraps negative bearing", () => {
      setBearing(-10)
      expect(mockEaseTo).toHaveBeenCalledWith(expect.objectContaining({ bearing: 350 }))
    })
  })

  describe("initRotationControls", () => {
    it("appends rotation buttons to map container", () => {
      const container = document.createElement("div")
      mockGetContainer.mockReturnValue(container)

      initRotationControls()

      expect(container.children.length).toBe(1)
      const wrap = container.firstElementChild!
      expect(wrap.className).toContain("nars-rotation-control")
      expect(wrap.children.length).toBe(2)
      expect(wrap.children[0].textContent).toBe("↺")
      expect(wrap.children[1].textContent).toBe("↻")
    })

    it("ccw button rotates counter-clockwise", () => {
      const container = document.createElement("div")
      mockGetContainer.mockReturnValue(container)

      setBearing(90)
      mockEaseTo.mockReset()

      initRotationControls()
      const ccw = container.querySelector("button")!
      ccw.click()

      expect(mockEaseTo).toHaveBeenCalledWith(expect.objectContaining({ bearing: 85 }))
    })

    it("cw button rotates clockwise", () => {
      const container = document.createElement("div")
      mockGetContainer.mockReturnValue(container)

      setBearing(90)
      mockEaseTo.mockReset()

      initRotationControls()
      const cw = container.querySelectorAll("button")[1]
      cw.click()

      expect(mockEaseTo).toHaveBeenCalledWith(expect.objectContaining({ bearing: 95 }))
    })
  })
})
