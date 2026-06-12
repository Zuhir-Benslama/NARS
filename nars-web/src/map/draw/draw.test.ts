import { describe, it, expect, beforeEach } from "vitest"
import { PHASES } from "../../phases"
import { pointToSegmentDist } from "./draw-handlers"
import { normalizeGeometry, getFeatureStyle } from "./draw-save"
import {
  resetDrawState,
  isSavingFeature,
  setSavingFeature,
  getDrawingPhase,
  setDrawingPhase,
  isSnappingEnabled,
  setSnappingEnabled,
} from "./draw-state"
import { resetDrawControl } from "./draw-control"
import type { Phase } from "../../types"
import type { ModalResult } from "../../types/modal"

describe("draw-handlers.ts", () => {
  describe("pointToSegmentDist", () => {
    it("returns perpendicular distance to segment", () => {
      // Horizontal segment from (0,10) to (10,10), cursor at (5, 5)
      // Distance should be 5 (vertical distance)
      const dist = pointToSegmentDist(5, 5, 0, 10, 10, 10)
      expect(dist).toBeCloseTo(5, 5)
    })

    it("returns distance to start endpoint when projection is before segment", () => {
      // Segment from (10,10) to (20,20), cursor at (0, 0)
      // Closest point is (10,10), distance = sqrt(200) ≈ 14.14
      const dist = pointToSegmentDist(0, 0, 10, 10, 20, 20)
      expect(dist).toBeCloseTo(Math.sqrt(200), 5)
    })

    it("returns distance to end endpoint when projection is after segment", () => {
      // Segment from (0,0) to (10,10), cursor at (20, 20)
      // Closest point is (10,10), distance = sqrt(200) ≈ 14.14
      const dist = pointToSegmentDist(20, 20, 0, 0, 10, 10)
      expect(dist).toBeCloseTo(Math.sqrt(200), 5)
    })

    it("handles degenerate segment (zero length)", () => {
      // Zero-length segment at (5,5), cursor at (10,10)
      const dist = pointToSegmentDist(10, 10, 5, 5, 5, 5)
      expect(dist).toBeCloseTo(Math.sqrt(50), 5)
    })

    it("returns zero when cursor is exactly on the segment", () => {
      // Segment from (0,0) to (10,0), cursor at (5, 0)
      const dist = pointToSegmentDist(5, 0, 0, 0, 10, 0)
      expect(dist).toBeCloseTo(0, 5)
    })
  })
})

describe("draw-save.ts", () => {
  describe("normalizeGeometry", () => {
    it("converts Polygon to LineString for polyline drawType", () => {
      const polygon: GeoJSON.Polygon = {
        type: "Polygon",
        coordinates: [
          [
            [0, 0],
            [1, 0],
            [1, 1],
            [0, 1],
            [0, 0],
          ],
        ],
      }
      const result = normalizeGeometry(polygon, "polyline")
      expect(result.type).toBe("LineString")
      expect((result as GeoJSON.LineString).coordinates).toHaveLength(5)
    })

    it("converts LineString to Polygon for polygon drawType by closing ring", () => {
      const line: GeoJSON.LineString = {
        type: "LineString",
        coordinates: [
          [0, 0],
          [1, 0],
          [1, 1],
          [0, 1],
        ],
      }
      const result = normalizeGeometry(line, "polygon") as GeoJSON.Polygon
      expect(result.type).toBe("Polygon")
      const ring = result.coordinates[0]
      // Should have the closing coordinate appended
      expect(ring).toHaveLength(5)
      expect(ring[ring.length - 1]).toEqual([0, 0])
    })

    it("passes through Point for marker drawType unchanged", () => {
      const point: GeoJSON.Point = { type: "Point", coordinates: [1, 2] }
      const result = normalizeGeometry(point, "marker") as GeoJSON.Point
      expect(result.type).toBe("Point")
      expect(result.coordinates).toEqual([1, 2])
    })

    it("passes through geometry when drawType already matches", () => {
      const polygon: GeoJSON.Polygon = {
        type: "Polygon",
        coordinates: [
          [
            [0, 0],
            [1, 0],
            [1, 1],
            [0, 1],
            [0, 0],
          ],
        ],
      }
      const result = normalizeGeometry(polygon, "polygon")
      expect(result.type).toBe("Polygon")
    })
  })

  describe("getFeatureStyle", () => {
    const phases = PHASES as Phase[]

    it("returns areas style with areaTypeKey", () => {
      const phase = phases.find((p) => p.key === "areas")!
      const modalResult = { areaTypeKey: "central_urban" } as ModalResult
      const style = getFeatureStyle(phase, modalResult)
      expect(style.fillColor).toBeDefined()
      expect(style.lineColor).toBeDefined()
      expect(style.lineWidth).toBeDefined()
    })

    it("returns districts style", () => {
      const phase = phases.find((p) => p.key === "districts")!
      const modalResult = {} as ModalResult
      const style = getFeatureStyle(phase, modalResult)
      expect(style.lineColor).toBe("#f39c12")
      expect(style.lineWidth).toBe(3)
    })

    it("returns publicBuildings style", () => {
      const phase = phases.find((p) => p.key === "publicBuildings")!
      const modalResult = {} as ModalResult
      const style = getFeatureStyle(phase, modalResult)
      expect(style.fillColor).toBe("#e67e22")
      expect(style.fillOpacity).toBe(0.25)
      expect(style.lineWidth).toBe(3)
    })

    it("returns publicSpaces style", () => {
      const phase = phases.find((p) => p.key === "publicSpaces")!
      const modalResult = {} as ModalResult
      const style = getFeatureStyle(phase, modalResult)
      expect(style.fillColor).toBe("#2ecc71")
      expect(style.fillOpacity).toBe(0.2)
      expect(style.lineWidth).toBe(3)
    })

    it("returns polyline style for road phase", () => {
      const phase = phases.find((p) => p.key === "roads")!
      const modalResult = {} as ModalResult
      const style = getFeatureStyle(phase, modalResult)
      expect(style.lineColor).toBe("#3498db")
      expect(style.lineWidth).toBe(8)
      expect(style.fillColor).toBeUndefined()
    })

    it("returns houseEntrances marker style", () => {
      const phase = phases.find((p) => p.key === "houseEntrances")!
      const modalResult = {} as ModalResult
      const style = getFeatureStyle(phase, modalResult)
      expect(style.circleColor).toBe("#27ae60")
      expect(style.circleRadius).toBe(10)
      expect(style.textColor).toBe("#000000")
    })

    it("returns default style for unknown phase", () => {
      const phase = phases.find((p) => p.key === "namingPanels")!
      const modalResult = {} as ModalResult
      const style = getFeatureStyle(phase, modalResult)
      expect(style.fillColor).toBe("#9b59b6")
      expect(style.fillOpacity).toBe(0.1)
      expect(style.lineColor).toBe("#9b59b6")
      expect(style.lineWidth).toBe(2)
      expect(style.circleColor).toBe("#9b59b6")
      expect(style.circleRadius).toBe(8)
    })
  })
})

describe("draw-state.ts", () => {
  beforeEach(() => {
    resetDrawState()
  })

  describe("save guard", () => {
    it("starts with saving = false", () => {
      expect(isSavingFeature()).toBe(false)
    })

    it("setSavingFeature toggles the flag", () => {
      setSavingFeature(true)
      expect(isSavingFeature()).toBe(true)
      setSavingFeature(false)
      expect(isSavingFeature()).toBe(false)
    })
  })

  describe("draw phase", () => {
    it("starts with null phase", () => {
      expect(getDrawingPhase()).toBeNull()
    })

    it("setDrawingPhase stores and retrieves the phase", () => {
      const phase = PHASES[0]
      setDrawingPhase(phase)
      expect(getDrawingPhase()).toEqual(phase)
    })

    it("setDrawingPhase(null) clears the phase", () => {
      setDrawingPhase(PHASES[1])
      setDrawingPhase(null)
      expect(getDrawingPhase()).toBeNull()
    })
  })

  describe("snapping enabled", () => {
    it("starts with snapping enabled", () => {
      expect(isSnappingEnabled()).toBe(true)
    })

    it("setSnappingEnabled toggles the flag", () => {
      setSnappingEnabled(false)
      expect(isSnappingEnabled()).toBe(false)
      setSnappingEnabled(true)
      expect(isSnappingEnabled()).toBe(true)
    })
  })

  describe("resetDrawState", () => {
    it("resets all state to defaults", () => {
      setSavingFeature(true)
      setDrawingPhase(PHASES[0])
      setSnappingEnabled(false)
      resetDrawState()
      expect(isSavingFeature()).toBe(false)
      expect(getDrawingPhase()).toBeNull()
      expect(isSnappingEnabled()).toBe(true)
    })
  })
})

describe("draw-control.ts", () => {
  describe("resetDrawControl", () => {
    it("resets module state without throwing", () => {
      expect(() => resetDrawControl()).not.toThrow()
    })

    it("can be called multiple times safely", () => {
      resetDrawControl()
      resetDrawControl()
      expect(() => resetDrawControl()).not.toThrow()
    })
  })
})
