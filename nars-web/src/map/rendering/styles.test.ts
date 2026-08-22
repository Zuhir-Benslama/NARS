import { describe, it, expect } from "vitest"
import { areaStyle } from "./styles"

describe("styles", () => {
  describe("areaStyle", () => {
    it("returns style for known area type", () => {
      const style = areaStyle("central_urban")
      expect(style).toHaveProperty("fillColor")
      expect(style).toHaveProperty("lineColor")
      expect(style).toHaveProperty("lineWidth", 2.5)
      expect(style).toHaveProperty("fillOpacity", 0)
    })

    it("falls back to first AREA_TYPE for unknown key", () => {
      const style = areaStyle("non_existent_type")
      expect(style).toBeDefined()
      expect(style.lineWidth).toBe(2.5)
    })
  })
})
