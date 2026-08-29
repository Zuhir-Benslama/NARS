import { describe, it, expect, vi, beforeEach, afterEach, type Mock } from "vitest"
import type { Map as MapLibreMap } from "maplibre-gl"
import { setActivePinia, createPinia } from "pinia"

const mockShowToast = vi.hoisted(() => vi.fn())

interface Listener {
  event: string
  layer?: string
  fn: (ev?: any) => void
}

interface MapMock {
  listeners: Listener[]
  on: Mock<(event: string, layer: string, fn: Listener["fn"]) => void>
  off: Mock<(event: string, layer: string, fn: Listener["fn"]) => void>
  getCanvas: Mock<() => { style: { setProperty: Mock; removeProperty: Mock } }>
  fire: (event: string, ev?: any) => void
}

let popupInstance: any

const MockPopup = vi.fn(function (this: any) {
  popupInstance = this
  this.setLngLat = vi.fn().mockReturnValue(this)
  this.setHTML = vi.fn().mockReturnValue(this)
  this.addTo = vi.fn().mockReturnValue(this)
  this.remove = vi.fn()
})

vi.mock("maplibre-gl", () => ({ default: { Popup: MockPopup } }))
vi.mock("../lib/toast", () => ({ showToast: mockShowToast }))

let mapMock: MapMock
let mod: typeof import("./map-boundary")

function makeMap(): MapMock {
  const listeners: Listener[] = []
  const canvas = { style: { setProperty: vi.fn(), removeProperty: vi.fn() } }
  return {
    listeners,
    on: vi.fn((event: string, layer: string, fn: Listener["fn"]) => {
      listeners.push({ event, layer, fn })
    }),
    off: vi.fn(),
    getCanvas: vi.fn(() => canvas),
    fire: (event: string, ev?: any) => {
      for (const l of listeners) if (l.event === event) l.fn(ev)
    },
  }
}

function toMap(map: MapMock): MapLibreMap {
  return map as unknown as MapLibreMap
}

beforeEach(async () => {
  vi.clearAllMocks()
  setActivePinia(createPinia())
  mapMock = makeMap()
  mod = await import("./map-boundary")
  mod.resetBoundaryEvents()
})

afterEach(() => {
  document.body.innerHTML = ""
  vi.restoreAllMocks()
})

describe("addBoundaryClickEvents / removeBoundaryClickEvents", () => {
  it("registers boundary listeners once", () => {
    mod.addBoundaryClickEvents(toMap(mapMock))
    expect(mapMock.on).toHaveBeenCalled()
    expect(mapMock.listeners.map((l) => l.event)).toEqual([
      "click",
      "mouseenter",
      "mouseleave",
      "contextmenu",
    ])
    mod.addBoundaryClickEvents(toMap(mapMock))
    expect(mapMock.on).toHaveBeenCalledTimes(4)
  })

  it("removes listeners and resets registration", () => {
    mod.addBoundaryClickEvents(toMap(mapMock))
    mod.removeBoundaryClickEvents()
    expect(mapMock.off).toHaveBeenCalled()
    expect(mapMock.off.mock.calls.map((c) => c[0])).toEqual([
      "click",
      "mouseenter",
      "mouseleave",
      "contextmenu",
    ])
    const next = makeMap()
    mod.addBoundaryClickEvents(toMap(next))
    expect(next.listeners.length).toBe(4)
  })
})

describe("onBoundaryClick", () => {
  it("shows a popup with the escaped commune name", () => {
    mod.addBoundaryClickEvents(toMap(mapMock))
    mapMock.fire("click", {
      features: [{ properties: { communeName: 'A"B<C>' } }],
      lngLat: { lng: 5, lat: 6 },
    })
    expect(MockPopup).toHaveBeenCalledTimes(1)
    expect(popupInstance.setHTML).toHaveBeenCalledWith(expect.stringContaining("B&lt;C&gt;"))
    expect(popupInstance.addTo).toHaveBeenCalledWith(mapMock)
  })

  it("falls back to the default label when no feature is present", () => {
    mod.addBoundaryClickEvents(toMap(mapMock))
    mapMock.fire("click", { features: undefined, lngLat: { lng: 1, lat: 2 } })
    expect(popupInstance.setHTML).toHaveBeenCalled()
  })

  it("reuses a single popup across rapid clicks", () => {
    mod.addBoundaryClickEvents(toMap(mapMock))
    mapMock.fire("click", {
      features: [{ properties: { communeName: "A" } }],
      lngLat: { lng: 1, lat: 1 },
    })
    const first = popupInstance
    mapMock.fire("click", {
      features: [{ properties: { communeName: "B" } }],
      lngLat: { lng: 2, lat: 2 },
    })
    expect(first.remove).toHaveBeenCalled()
  })
})

describe("onBoundaryEnter / onBoundaryLeave", () => {
  it("sets and clears the pointer cursor", () => {
    mod.addBoundaryClickEvents(toMap(mapMock))
    mapMock.fire("mouseenter", {})
    expect(mapMock.getCanvas().style.setProperty).toHaveBeenCalledWith(
      "cursor",
      "pointer",
      "important",
    )
    mapMock.fire("mouseleave", {})
    expect(mapMock.getCanvas().style.removeProperty).toHaveBeenCalledWith("cursor")
  })
})

describe("onBoundaryContextMenu", () => {
  it("builds a menu and copies the commune name on click", async () => {
    const clipboard = { writeText: vi.fn().mockResolvedValue(undefined) }
    Object.assign(navigator, { clipboard })

    mod.addBoundaryClickEvents(toMap(mapMock))
    const preventDefault = vi.fn()
    mapMock.fire("contextmenu", {
      preventDefault,
      originalEvent: { preventDefault: vi.fn() },
      point: { x: 10, y: 10 },
      features: [{ properties: { communeName: "Royaume" } }],
    })
    expect(preventDefault).toHaveBeenCalled()

    await new Promise((r) => setTimeout(r, 40))
    const menu = document.getElementById("nars-boundary-ctx-menu")
    expect(menu).not.toBeNull()

    const copyItem = menu!.querySelector('[data-action="copy-name"]') as HTMLElement
    copyItem.click()
    await new Promise((r) => setTimeout(r, 0))
    expect(clipboard.writeText).toHaveBeenCalledWith("Royaume")
    expect(mockShowToast).toHaveBeenCalled()
    expect(document.getElementById("nars-boundary-ctx-menu")).toBeNull()
  })

  it("keeps the menu on screen when near the right edge", async () => {
    Object.assign(navigator, { clipboard: { writeText: vi.fn().mockResolvedValue(undefined) } })
    vi.spyOn(window, "innerWidth", "get").mockReturnValue(200)
    vi.spyOn(window, "innerHeight", "get").mockReturnValue(200)

    mod.addBoundaryClickEvents(toMap(mapMock))
    mapMock.fire("contextmenu", {
      preventDefault: vi.fn(),
      point: { x: 190, y: 190 },
      features: [{ properties: { communeName: "Edge" } }],
    })
    await new Promise((r) => setTimeout(r, 40))
    const menu = document.getElementById("nars-boundary-ctx-menu") as HTMLElement
    expect(menu.style.left).toBe("10px")
    expect(menu.style.top).toBe("90px")
  })
})
