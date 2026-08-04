import { describe, it, expect, vi, beforeEach } from "vitest"
import { mount } from "@vue/test-utils"
import { setActivePinia, createPinia } from "pinia"

vi.mock("vue-i18n", () => ({
  useI18n: () => ({ t: (key: string) => key }),
}))

import InfoPanel from "./InfoPanel.vue"
import { useLayerStore } from "../stores/layerStore"
import type { LayerEntry } from "../types"

function entrance(label: string, type: "main_entrance" | "secondary_entrance"): LayerEntry {
  return {
    id: `e-${label}`,
    dbId: `e-${label}`,
    type: "marker",
    data: { type: "houseEntrances", label, entranceTypeKey: type },
  } as LayerEntry
}

describe("InfoPanel", () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it("renders info title", () => {
    const wrapper = mount(InfoPanel)
    expect(wrapper.text()).toContain("info_title")
  })

  it("displays counts derived from the layer store", () => {
    const store = useLayerStore()
    store.addFeature("areas", {
      id: "a1",
      dbId: "a1",
      type: "polygon",
      data: { type: "areas", label: "A1" },
    } as LayerEntry)
    store.addFeature("districts", {
      id: "d1",
      dbId: "d1",
      type: "polygon",
      data: { type: "districts", label: "D1" },
    } as LayerEntry)
    store.addFeature("roads", {
      id: "r1",
      dbId: "r1",
      type: "line",
      data: { type: "roads", label: "R1" },
    } as LayerEntry)
    store.addFeature("houseEntrances", entrance("E1", "main_entrance"))
    store.addFeature("houseEntrances", entrance("E2", "main_entrance"))
    store.addFeature("houseEntrances", entrance("E3", "secondary_entrance"))
    store.addFeature("publicBuildings", {
      id: "pb1",
      dbId: "pb1",
      type: "polygon",
      data: { type: "publicBuildings", label: "PB1" },
    } as LayerEntry)
    store.addFeature("publicSpaces", {
      id: "ps1",
      dbId: "ps1",
      type: "polygon",
      data: { type: "publicSpaces", label: "PS1" },
    } as LayerEntry)

    const wrapper = mount(InfoPanel)
    expect(wrapper.text()).toContain("1")
    expect(wrapper.text()).toContain("2")
    expect(wrapper.text()).toContain("—")
  })

  it('shows "placed" for city center when one exists', () => {
    const store = useLayerStore()
    store.addFeature("cityCenter", {
      id: "cc1",
      dbId: "cc1",
      type: "circle",
      data: { type: "cityCenter", label: "CC", lat: 36.7, lng: 3.1, radius: 500 },
    } as LayerEntry)
    const wrapper = mount(InfoPanel)
    expect(wrapper.text()).toContain("info_status_placed")
  })

  it("shows em dash for city center when not placed", () => {
    const wrapper = mount(InfoPanel)
    expect(wrapper.text()).toContain("—")
  })

  it("shows zero counts by default", () => {
    const wrapper = mount(InfoPanel)
    expect(wrapper.text()).toContain("0")
  })
})
