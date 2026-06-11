import { describe, it, expect } from "vitest"
import type maplibregl from "maplibre-gl"
import {
  closestOnSegment,
  closestOnCirclePerimeter,
  pixelDist,
  closestOnSegmentProjected,
} from "./snap-geometry"

const project = (ll: [number, number]) => ({ x: ll[0], y: -ll[1] })
const unproject = (pt: [number, number]) =>
  ({ lng: pt[0], lat: -pt[1] }) as unknown as maplibregl.LngLat

describe("snap-geometry", () => {
  describe("closestOnSegment", () => {
    it("returns the endpoint when segment is degenerate", () => {
      const result = closestOnSegment(0, 0, 10, 20, 10, 20, project, unproject)
      expect(result).not.toBeNull()
      expect(result!.x).toBe(10)
      expect(result!.y).toBe(-20)
      expect(result!.lng).toBe(10)
      expect(result!.lat).toBe(20)
    })

    it("returns the closest point on a horizontal segment", () => {
      const result = closestOnSegment(15, 0, 10, 0, 20, 0, project, unproject)
      expect(result).not.toBeNull()
      expect(result!.lng).toBeCloseTo(15, 1)
      expect(result!.lat).toBeCloseTo(0, 1)
    })

    it("returns null when project/unproject throw", () => {
      const badProject = () => {
        throw new Error("map not ready")
      }
      const result = closestOnSegment(0, 0, 1, 2, 3, 4, badProject, unproject)
      expect(result).toBeNull()
    })

    it("clamps the closest point to segment endpoints", () => {
      const result = closestOnSegment(100, 0, 10, 0, 20, 0, project, unproject)
      expect(result).not.toBeNull()
      expect(result!.lng).toBeCloseTo(20, 1)
    })
  })

  describe("pixelDist", () => {
    it("computes pixel distance between cursor and projected point", () => {
      const dist = pixelDist(10, 10, 5, 5, project)
      expect(dist).not.toBeNull()
      expect(dist).toBeCloseTo(Math.hypot(10 - 5, 10 - (-5)), 5)
    })

    it("returns null on projection failure", () => {
      const badProject = () => {
        throw new Error("fail")
      }
      const dist = pixelDist(0, 0, 1, 2, badProject)
      expect(dist).toBeNull()
    })

    it("returns 0 when cursor is exactly on the point", () => {
      const dist = pixelDist(10, -20, 10, 20, project)
      expect(dist).toBe(0)
    })
  })

  describe("closestOnSegmentProjected", () => {
    it("returns endpoint for degenerate segment", () => {
      const result = closestOnSegmentProjected(0, 0, 5, 5, 5, 5, 10, 5, unproject)
      expect(result).not.toBeNull()
      expect(result!.lng).toBe(5)
      expect(result!.lat).toBe(10)
    })

    it("finds closest point on projected segment", () => {
      const result = closestOnSegmentProjected(15, 0, 10, 0, 20, 0, 0, 10, unproject)
      expect(result).not.toBeNull()
      expect(result!.x).toBeCloseTo(15, 1)
      expect(result!.y).toBeCloseTo(0, 1)
    })

    it("returns null when unproject throws", () => {
      const badUnproject = () => {
        throw new Error("fail")
      }
      const result = closestOnSegmentProjected(0, 0, 1, 2, 3, 4, 0, 0, badUnproject)
      expect(result).toBeNull()
    })
  })

  describe("closestOnCirclePerimeter", () => {
    it("returns point on circle perimeter closest to cursor", () => {
      const projectIdentity = (ll: [number, number]) => ({ x: ll[0], y: ll[1] })
      const unprojectIdentity = (pt: [number, number]) =>
        ({ lng: pt[0], lat: pt[1] }) as unknown as maplibregl.LngLat
      const result = closestOnCirclePerimeter(
        20,
        0,
        10,
        0,
        1000,
        projectIdentity,
        unprojectIdentity,
      )
      expect(result).not.toBeNull()
      expect(result!.lng).toBeCloseTo(10, 1)
      expect(result!.lat).toBeCloseTo(0, 1)
    })

    it("returns null for zero radius", () => {
      const result = closestOnCirclePerimeter(0, 0, 0, 0, 0, project, unproject)
      expect(result).toBeNull()
    })

    it("returns null when cursor is at center", () => {
      const projectIdentity = (ll: [number, number]) => ({ x: ll[0], y: ll[1] })
      const unprojectIdentity = (pt: [number, number]) =>
        ({ lng: pt[0], lat: pt[1] }) as unknown as maplibregl.LngLat
      const result = closestOnCirclePerimeter(
        10,
        0,
        10,
        0,
        100,
        projectIdentity,
        unprojectIdentity,
      )
      expect(result).toBeNull()
    })

    it("returns null when project throws", () => {
      const badProject = () => {
        throw new Error("fail")
      }
      const result = closestOnCirclePerimeter(0, 0, 1, 2, 100, badProject, unproject)
      expect(result).toBeNull()
    })
  })
})
