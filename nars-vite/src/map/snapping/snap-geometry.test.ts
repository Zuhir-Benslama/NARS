// ─── SNAP-GEOMETRY.TS TESTS ──────────────────────────────────────────────────
// Unit tests for pure geometry functions used by the snap engine.
// These are stateless, projection-based calculations — ideal for unit testing.

import { describe, it, expect, vi } from "vitest"
import {
  closestOnSegment,
  closestOnCirclePerimeter,
  pixelDist,
  closestOnSegmentProjected,
} from "./snap-geometry"

// Mock MapLibre project/unproject
function makeProject() {
  // Simple linear projection mock: lng→x, lat→y with scale
  const scale = 1000
  const makeLngLat = (x: number, y: number) => ({
    lng: x / scale,
    lat: y / scale,
    wrap() {
      return this
    },
    toArray: (): [number, number] => [x / scale, y / scale],
    distanceTo: () => 0,
  })
  return {
    project: ([lng, lat]: [number, number]) => ({
      x: lng * scale,
      y: lat * scale,
    }),
    unproject: ([x, y]: [number, number]) => makeLngLat(x, y),
  }
}

describe("snap-geometry.ts", () => {
  describe("closestOnSegment", () => {
    it("returns endpoint when cursor is before segment start", () => {
      const { project, unproject } = makeProject()
      const result = closestOnSegment(0, 0, 0.001, 0.001, 0.002, 0.002, project, unproject)
      expect(result).not.toBeNull()
      expect(result!.x).toBeCloseTo(0.001 * 1000)
      expect(result!.y).toBeCloseTo(0.001 * 1000)
    })

    it("returns closest point on segment for cursor alongside", () => {
      const { project, unproject } = makeProject()
      // Segment from (0,0) to (2,2), cursor at (1, 0.5)
      const result = closestOnSegment(1000, 500, 0, 0, 0.002, 0.002, project, unproject)
      expect(result).not.toBeNull()
      // Closest point should be on the segment, between endpoints
      expect(result!.x).toBeGreaterThan(0)
      expect(result!.x).toBeLessThan(2000)
    })

    it("returns endpoint when cursor is past segment end", () => {
      const { project, unproject } = makeProject()
      const result = closestOnSegment(3000, 3000, 0, 0, 0.002, 0.002, project, unproject)
      expect(result).not.toBeNull()
      expect(result!.x).toBeCloseTo(0.002 * 1000)
      expect(result!.y).toBeCloseTo(0.002 * 1000)
    })

    it("handles degenerate segment (zero length)", () => {
      const { project, unproject } = makeProject()
      const result = closestOnSegment(500, 500, 0.001, 0.001, 0.001, 0.001, project, unproject)
      expect(result).not.toBeNull()
      expect(result!.x).toBeCloseTo(0.001 * 1000)
      expect(result!.y).toBeCloseTo(0.001 * 1000)
    })
  })

  describe("closestOnSegmentProjected", () => {
    it("returns closest point for cursor alongside segment", () => {
      const { unproject } = makeProject()
      // Segment from (0,0) to (1000,1000) in pixel space
      const result = closestOnSegmentProjected(500, 0, 0, 0, 1000, 1000, 0, 0, unproject)
      expect(result).not.toBeNull()
      expect(result!.x).toBeCloseTo(250, 0)
      expect(result!.y).toBeCloseTo(250, 0)
    })

    it("handles degenerate segment (zero length)", () => {
      const { unproject } = makeProject()
      const result = closestOnSegmentProjected(
        100,
        100,
        500,
        500,
        500,
        500,
        0.001,
        0.001,
        unproject,
      )
      expect(result).not.toBeNull()
      expect(result!.x).toBe(500)
      expect(result!.y).toBe(500)
    })

    it("returns null on unproject failure", () => {
      const unproject = vi.fn(() => {
        throw new Error("Projection failed")
      })
      const result = closestOnSegmentProjected(500, 500, 0, 0, 1000, 1000, 0, 0, unproject as any)
      expect(result).toBeNull()
    })
  })

  describe("closestOnCirclePerimeter", () => {
    it("returns point on circle perimeter towards cursor", () => {
      const { project, unproject } = makeProject()
      // Circle at (1, 1) with 100m radius
      const result = closestOnCirclePerimeter(1500, 1000, 1, 1, 100, project, unproject)
      expect(result).not.toBeNull()
      expect(result!.dist).toBeGreaterThanOrEqual(0)
    })

    it("returns null when cursor is at center", () => {
      const { project, unproject } = makeProject()
      const result = closestOnCirclePerimeter(1000, 1000, 1, 1, 100, project, unproject)
      expect(result).toBeNull()
    })

    it("returns null for zero radius", () => {
      const { project, unproject } = makeProject()
      const result = closestOnCirclePerimeter(1500, 1000, 1, 1, 0, project, unproject)
      expect(result).toBeNull()
    })

    it("handles negative radius by returning a point on the opposite side", () => {
      const { project, unproject } = makeProject()
      // Negative radius produces a point at |radius| on opposite side
      const result = closestOnCirclePerimeter(1500, 1000, 1, 1, -50, project, unproject)
      expect(result).not.toBeNull()
      expect(result!.dist).toBeGreaterThanOrEqual(0)
    })
  })

  describe("pixelDist", () => {
    it("returns correct pixel distance", () => {
      const { project } = makeProject()
      // Point at (0.001, 0.001) projects to (1, 1) in the mock's coordinate space
      // Cursor at (0, 0) — distance = sqrt(1² + 1²) = sqrt(2)
      const dist = pixelDist(0, 0, 0.001, 0.001, project)
      expect(dist).not.toBeNull()
      expect(dist!).toBeCloseTo(Math.sqrt(2), 5)
    })

    it("returns zero for coincident points", () => {
      const { project } = makeProject()
      const dist = pixelDist(1000, 1000, 1, 1, project)
      expect(dist).not.toBeNull()
      expect(dist!).toBeCloseTo(0)
    })

    it("returns null on projection failure", () => {
      const project = vi.fn(() => {
        throw new Error("Projection failed")
      })
      const dist = pixelDist(0, 0, 1, 1, project as any)
      expect(dist).toBeNull()
    })
  })
})
