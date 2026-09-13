import { beforeEach, describe, expect, it, vi } from "vitest"
import { mount, flushPromises } from "@vue/test-utils"
import { createPinia, setActivePinia } from "pinia"
import type { AiDraftFeatureDto } from "../api/drafts"
import type { UserInfo } from "../types/user"

const mocks = vi.hoisted(() => {
  const setData = vi.fn()
  return {
    setData,
    reviewDraft: vi.fn(),
    startDraftEdit: vi.fn(),
    listDrafts: vi.fn(),
    fitBounds: vi.fn(),
    ctx: undefined as unknown,
  }
})

vi.mock("vue-i18n", () => ({
  useI18n: () => ({ t: (key: string) => key }),
}))

vi.mock("../map/drafts/review-actions", () => ({ reviewDraft: mocks.reviewDraft }))

vi.mock("../map/drafts/draft-edit", () => ({ startDraftEdit: mocks.startDraftEdit }))

vi.mock("../api/drafts", () => ({ listDrafts: mocks.listDrafts }))

vi.mock("../map/core/state", () => ({
  tryGetCtx: () => mocks.ctx,
  getCtx: () => mocks.ctx,
}))

import DraftsPanel from "./DraftsPanel.vue"
import { useDraftsStore } from "../stores/draftsStore"
import { useAppStore } from "../stores/appStore"

const ROAD_DRAFT: AiDraftFeatureDto = {
  id: "d1",
  featureType: "road",
  geometryGeoJson: '{"type":"LineString","coordinates":[[36.72,2.96],[36.73,2.97]]}',
  confidence: 0.9,
  status: "pending",
  createdAt: "2026-01-01T00:00:00Z",
}

const BUILDING_DRAFT: AiDraftFeatureDto = {
  id: "d2",
  featureType: "building",
  geometryGeoJson:
    '{"type":"Polygon","coordinates":[[[36.71,2.95],[36.72,2.95],[36.72,2.96],[36.71,2.95]]]}',
  confidence: "0.905",
  status: "pending",
  createdAt: "2026-01-01T00:00:00Z",
}

const globalOpts = {
  stubs: { Teleport: { template: "<div><slot /></div>" } },
}

function user(communeId: number | null): UserInfo {
  return {
    id: 1,
    username: "jdoe",
    name: "John Doe",
    email: "jdoe@test.com",
    role: "field_worker",
    commune: { id: communeId, name_fr: null, name_ar: null, latitude: null, longitude: null },
  }
}

describe("DraftsPanel", () => {
  beforeEach(() => {
    vi.clearAllMocks()
    setActivePinia(createPinia())
    mocks.ctx = { draftsSource: { setData: mocks.setData }, map: { fitBounds: mocks.fitBounds } }
  })

  it("renders the toggle and shows the pending count as a badge", async () => {
    const store = useDraftsStore()
    store.drafts = [ROAD_DRAFT, BUILDING_DRAFT]
    const wrapper = mount(DraftsPanel, { global: globalOpts })

    const toggle = wrapper.find(".drafts-toggle")
    expect(toggle.exists()).toBe(true)
    expect(toggle.text()).toContain("draft_panel_title")
    expect(wrapper.find(".drafts-badge").text()).toBe("2")
    expect(wrapper.find(".drafts-panel").exists()).toBe(false)
  })

  it("opens the panel and lists drafts with type + rounded confidence", async () => {
    const store = useDraftsStore()
    store.drafts = [ROAD_DRAFT, BUILDING_DRAFT]
    const wrapper = mount(DraftsPanel, { global: globalOpts })

    await wrapper.find(".drafts-toggle").trigger("click")

    const items = wrapper.findAll(".drafts-item")
    expect(items).toHaveLength(2)
    expect(items[0].text()).toContain("draft_panel_road")
    expect(items[0].text()).toContain("90%")
    expect(items[1].text()).toContain("draft_panel_building")
    expect(items[1].text()).toContain("91%")
  })

  it("shows the loading indicator instead of the list while loading", async () => {
    const store = useDraftsStore()
    store.drafts = [ROAD_DRAFT]
    store.loading = true
    const wrapper = mount(DraftsPanel, { global: globalOpts })

    await wrapper.find(".drafts-toggle").trigger("click")

    expect(wrapper.find(".drafts-loading").exists()).toBe(true)
    expect(wrapper.find(".drafts-list").exists()).toBe(false)
  })

  it("shows the empty state when there are no pending drafts", async () => {
    const wrapper = mount(DraftsPanel, { global: globalOpts })

    await wrapper.find(".drafts-toggle").trigger("click")

    expect(wrapper.find(".drafts-empty").exists()).toBe(true)
  })

  it("wires the accept button to reviewDraft", async () => {
    const store = useDraftsStore()
    store.drafts = [ROAD_DRAFT]
    const wrapper = mount(DraftsPanel, { global: globalOpts })
    await wrapper.find(".drafts-toggle").trigger("click")

    await wrapper.find(".drafts-action.is-accept").trigger("click")

    expect(mocks.reviewDraft).toHaveBeenCalledWith("d1", "accept")
  })

  it("wires reject and delete buttons to reviewDraft", async () => {
    const store = useDraftsStore()
    store.drafts = [ROAD_DRAFT]
    const wrapper = mount(DraftsPanel, { global: globalOpts })
    await wrapper.find(".drafts-toggle").trigger("click")

    await wrapper.find(".drafts-action.is-reject").trigger("click")
    await wrapper.find(".drafts-action.is-delete").trigger("click")

    expect(mocks.reviewDraft).toHaveBeenCalledWith("d1", "reject")
    expect(mocks.reviewDraft).toHaveBeenCalledWith("d1", "delete")
  })

  it("wires the edit button to startDraftEdit", async () => {
    const store = useDraftsStore()
    store.drafts = [ROAD_DRAFT]
    const wrapper = mount(DraftsPanel, { global: globalOpts })
    await wrapper.find(".drafts-toggle").trigger("click")

    await wrapper.find(".drafts-action.is-edit").trigger("click")

    expect(mocks.startDraftEdit).toHaveBeenCalledWith("d1")
  })

  it("highlights the selected draft", async () => {
    const store = useDraftsStore()
    store.drafts = [ROAD_DRAFT, BUILDING_DRAFT]
    store.setSelectedDraftId("d2")
    const wrapper = mount(DraftsPanel, { global: globalOpts })
    await wrapper.find(".drafts-toggle").trigger("click")

    const items = wrapper.findAll(".drafts-item")
    expect(items[1].classes()).toContain("selected")
  })

  it("centers the map on a road draft when selected", async () => {
    const store = useDraftsStore()
    store.drafts = [ROAD_DRAFT]
    const wrapper = mount(DraftsPanel, { global: globalOpts })
    await wrapper.find(".drafts-toggle").trigger("click")

    await wrapper.find(".drafts-item").trigger("click")

    expect(store.selectedDraftId).toBe("d1")
    expect(mocks.fitBounds).toHaveBeenCalledWith(
      [
        [36.72, 2.96],
        [36.73, 2.97],
      ],
      { padding: 60, maxZoom: 18 },
    )
  })

  it("centers the map on a building draft via its outer ring", async () => {
    const store = useDraftsStore()
    store.drafts = [BUILDING_DRAFT]
    const wrapper = mount(DraftsPanel, { global: globalOpts })
    await wrapper.find(".drafts-toggle").trigger("click")

    await wrapper.findAll(".drafts-item")[0].trigger("click")

    expect(store.selectedDraftId).toBe("d2")
    expect(mocks.fitBounds).toHaveBeenCalledWith(
      [
        [36.71, 2.95],
        [36.72, 2.96],
      ],
      { padding: 60, maxZoom: 18 },
    )
  })

  it("selects the draft but does not fit bounds when the map is missing", async () => {
    mocks.ctx = { draftsSource: { setData: mocks.setData } }
    const store = useDraftsStore()
    store.drafts = [ROAD_DRAFT]
    const wrapper = mount(DraftsPanel, { global: globalOpts })
    await wrapper.find(".drafts-toggle").trigger("click")

    await wrapper.find(".drafts-item").trigger("click")

    expect(store.selectedDraftId).toBe("d1")
    expect(mocks.fitBounds).not.toHaveBeenCalled()
  })

  it("does not fit bounds when the draft geometry cannot be parsed", async () => {
    const store = useDraftsStore()
    store.drafts = [{ ...ROAD_DRAFT, geometryGeoJson: "not json" }]
    const wrapper = mount(DraftsPanel, { global: globalOpts })
    await wrapper.find(".drafts-toggle").trigger("click")

    await wrapper.find(".drafts-item").trigger("click")

    expect(store.selectedDraftId).toBe("d1")
    expect(mocks.fitBounds).not.toHaveBeenCalled()
  })

  it("loads pending drafts for the user commune on mount", async () => {
    const appStore = useAppStore()
    appStore.setUser(user(7))
    mocks.listDrafts.mockResolvedValue([ROAD_DRAFT])

    mount(DraftsPanel, { global: globalOpts })
    await flushPromises()

    expect(mocks.listDrafts).toHaveBeenCalledWith({ communeId: 7, status: "pending" })
    expect(useDraftsStore().drafts).toHaveLength(1)
    expect(mocks.setData).toHaveBeenCalled()
  })

  it("does not load drafts on mount when the user has no commune", async () => {
    const appStore = useAppStore()
    appStore.setUser(user(null))

    mount(DraftsPanel, { global: globalOpts })
    await flushPromises()

    expect(mocks.listDrafts).not.toHaveBeenCalled()
  })

  it("reloads on the refresh button", async () => {
    const appStore = useAppStore()
    appStore.setUser(user(7))
    mocks.listDrafts.mockResolvedValue([])

    const wrapper = mount(DraftsPanel, { global: globalOpts })
    await flushPromises()
    await wrapper.find(".drafts-toggle").trigger("click")
    mocks.listDrafts.mockClear()
    mocks.listDrafts.mockResolvedValue([BUILDING_DRAFT])

    await wrapper.find(".drafts-refresh").trigger("click")
    await flushPromises()

    expect(mocks.listDrafts).toHaveBeenCalledWith({ communeId: 7, status: "pending" })
    expect(useDraftsStore().drafts).toHaveLength(1)
  })
})
