import { describe, it, expect } from "vitest"
import { areaStyle, createEntranceIconHtml, buildPopupContent } from "./styles"
import { PHASES } from "../../phases"

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

  describe("createEntranceIconHtml", () => {
    it("returns HTML with given label and default color", () => {
      const html = createEntranceIconHtml(42)
      expect(html).toContain("42")
      expect(html).toContain("#27ae60")
      expect(html).toContain("entrance-marker")
    })

    it("uses custom color when provided", () => {
      const html = createEntranceIconHtml("A", "#ff0000")
      expect(html).toContain("#ff0000")
    })

    it("handles empty label by showing ?", () => {
      const html = createEntranceIconHtml("")
      expect(html).toContain("?")
    })

    it("truncates labels longer than 6 characters", () => {
      const html = createEntranceIconHtml("1234567")
      expect(html).toContain("123456")
      expect(html).not.toContain(">1234567<")
    })

    it("adjusts width based on label length", () => {
      const short = createEntranceIconHtml("AB")
      expect(short).toContain("width: 16px")

      const medium = createEntranceIconHtml("ABCD")
      expect(medium).toContain("width: 22px")

      const long = createEntranceIconHtml("ABCDEF")
      expect(long).toContain("width: 28px")
    })

    it("sanitizes label text", () => {
      const html = createEntranceIconHtml("<script>")
      expect(html).not.toContain("<script>")
    })
  })

  describe("buildPopupContent", () => {
    const testPhase = PHASES[0]

    const base = { decisionNumber: "", decisionDate: "" }

    it("returns label and phase name with minimum data", () => {
      const html = buildPopupContent({ type: "areas", label: "Test Label", ...base }, testPhase)
      expect(html).toContain("Test Label")
    })

    it("includes decision number when present", () => {
      const html = buildPopupContent(
        { type: "areas", label: "X", ...base, decisionNumber: "2024/001" },
        testPhase,
      )
      expect(html).toContain("2024/001")
    })

    it("includes decision date when present", () => {
      const html = buildPopupContent(
        { type: "areas", label: "X", ...base, decisionDate: "2024-06-15" },
        testPhase,
      )
      expect(html).toContain("2024-06-15")
    })

    it("includes road label when present", () => {
      const html = buildPopupContent(
        { type: "roads", label: "X", ...base, roadLabel: "RN-1" },
        testPhase,
      )
      expect(html).toContain("RN-1")
    })

    it("includes side with odd/even hint", () => {
      const html = buildPopupContent(
        { type: "roads", label: "X", ...base, side: "left" },
        testPhase,
      )
      expect(html).toContain("left")
    })

    it("includes main entrance label when present", () => {
      const html = buildPopupContent(
        { type: "houseEntrances", label: "X", ...base, mainEntranceLabel: "E-001" },
        testPhase,
      )
      expect(html).toContain("E-001")
    })

    it("omits fields that are not present", () => {
      const html = buildPopupContent({ type: "areas", label: "Minimal", ...base }, testPhase)
      expect(html).not.toContain("popup_decision")
      expect(html).not.toContain("popup_date")
      expect(html).not.toContain("popup_road")
    })
  })
})
