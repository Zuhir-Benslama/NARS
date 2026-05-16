import { describe, it, expect } from "vitest"
import { PHASES, API_LAYER_TO_PHASE } from "./phases"

describe("PHASES", () => {
  it("has exactly 8 phases", () => {
    expect(PHASES).toHaveLength(8)
  })

  it("each phase has required fields", () => {
    for (const p of PHASES) {
      expect(p.index).toBeTypeOf("number")
      expect(p.key).toBeTypeOf("string")
      expect(p.label).toBeTypeOf("string")
      expect(p.drawType).toBeTypeOf("string")
      expect(p.color).toMatch(/^#[0-9a-f]{6}$/)
      expect(p.hint).toBeTypeOf("string")
      expect(p.geometryType).toBeTypeOf("string")
    }
  })

  it("phases are ordered sequentially", () => {
    for (let i = 0; i < PHASES.length; i++) {
      expect(PHASES[i].index).toBe(i)
    }
  })

  it("each phase has a unique key", () => {
    const keys = PHASES.map((p) => p.key)
    expect(new Set(keys).size).toBe(keys.length)
  })

  it("has unique colors across phases", () => {
    const colors = PHASES.map((p) => p.color)
    expect(new Set(colors).size).toBe(colors.length)
  })

  it("areas phase uses polygon drawType", () => {
    const areas = PHASES.find((p) => p.key === "areas")
    expect(areas?.drawType).toBe("polygon")
    expect(areas?.geometryType).toBe("Polygon")
  })

  it("cityCenter phase uses circle drawType and Point geometry", () => {
    const cc = PHASES.find((p) => p.key === "cityCenter")
    expect(cc?.drawType).toBe("circle")
    expect(cc?.geometryType).toBe("Point")
  })

  it("roads phase uses polyline drawType and LineString geometry", () => {
    const roads = PHASES.find((p) => p.key === "roads")
    expect(roads?.drawType).toBe("polyline")
    expect(roads?.geometryType).toBe("LineString")
  })

  it("namingPanels phase uses marker drawType and Point geometry", () => {
    const np = PHASES.find((p) => p.key === "namingPanels")
    expect(np?.drawType).toBe("marker")
    expect(np?.geometryType).toBe("Point")
  })
})

describe("API_LAYER_TO_PHASE", () => {
  it("maps central_urban to areas", () => {
    expect(API_LAYER_TO_PHASE["central_urban"]).toBe("areas")
  })

  it("maps secondary_urban to areas", () => {
    expect(API_LAYER_TO_PHASE["secondary_urban"]).toBe("areas")
  })

  it("maps city_center to cityCenter", () => {
    expect(API_LAYER_TO_PHASE["city_center"]).toBe("cityCenter")
  })

  it("maps all road types to roads", () => {
    const roadLayers = ["boulevard", "avenue", "street", "drive", "lane", "cul_de_sac"]
    for (const layer of roadLayers) {
      expect(API_LAYER_TO_PHASE[layer]).toBe("roads")
    }
  })

  it("maps naming_panel to namingPanels", () => {
    expect(API_LAYER_TO_PHASE["naming_panel"]).toBe("namingPanels")
  })

  it("maps all public building sub-types to publicBuildings", () => {
    const buildingLayers = ["bank", "post_office", "school", "mosque", "public_hospital", "stadium"]
    for (const layer of buildingLayers) {
      expect(API_LAYER_TO_PHASE[layer]).toBe("publicBuildings")
    }
  })

  it("every phase key has at least one corresponding API layer", () => {
    const phaseKeys = new Set(PHASES.map((p) => p.key))
    const mappedKeys = new Set(Object.values(API_LAYER_TO_PHASE))
    for (const key of phaseKeys) {
      expect(mappedKeys.has(key)).toBe(true)
    }
  })

  it("all layer values are valid phase keys", () => {
    const phaseKeys = new Set(PHASES.map((p) => p.key))
    for (const phaseKey of Object.values(API_LAYER_TO_PHASE)) {
      expect(phaseKeys.has(phaseKey)).toBe(true)
    }
  })
})
