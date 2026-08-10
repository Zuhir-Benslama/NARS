import { describe, it, expect, vi, beforeEach } from "vitest"
import { shallowMount } from "@vue/test-utils"
import { mockApiFetch, createMockSuccessResponse } from "../test/setup"

const routeState = vi.hoisted(() => ({ params: { wilayaName: "alger" } }))
const mockRouterPush = vi.hoisted(() => vi.fn())
const mockAppStore = vi.hoisted(() => vi.fn())

vi.mock("vue-router", () => ({
  useRoute: () => routeState,
  useRouter: () => ({ push: mockRouterPush }),
}))

vi.mock("vue-i18n", () => ({
  useI18n: () => ({ t: (key: string) => key }),
}))

vi.mock("../stores/appStore", () => ({
  useAppStore: () => mockAppStore(),
}))

import WilayaDetailPage from "./WilayaDetailPage.vue"
import DairaList from "./admin/DairaList.vue"

function makeNationalOverview() {
  return {
    wilayas: [
      {
        wilaya_id: 1,
        wilaya_name_fr: "Alger",
        wilaya_name_ar: "الجزائر",
        daira_count: 13,
        commune_count: 57,
        commune_user_count: 120,
        wilaya_admin: null,
        dairas: [],
      },
    ],
  }
}

function makeWilayaReport() {
  return {
    wilaya_id: 1,
    wilaya_name_fr: "Alger",
    wilaya_name_ar: "الجزائر",
    dairas: [
      {
        daira_id: 1,
        daira_name_fr: "Sidi M'Hamed",
        daira_name_ar: "سيدي امحمد",
        commune_count: 3,
        commune_user_count: 10,
        daira_admin: null,
        communes: [],
      },
    ],
  }
}

function flush() {
  return new Promise((resolve) => setTimeout(resolve, 10))
}

function mockRole(role: string) {
  mockAppStore.mockReturnValue({ user: { role } })
}

describe("WilayaDetailPage", () => {
  beforeEach(() => {
    mockApiFetch.mockReset()
    mockRouterPush.mockClear()
    routeState.params.wilayaName = "alger"
  })

  it("shows a loading state while the request is pending", async () => {
    mockRole("national_admin")
    mockApiFetch.mockImplementationOnce(() => new Promise(() => {}))
    const wrapper = shallowMount(WilayaDetailPage)
    await flush()
    expect(wrapper.find(".loading-state").exists()).toBe(true)
  })

  describe("national_admin", () => {
    it("fetches the overview, resolves the wilaya and loads its detail", async () => {
      mockRole("national_admin")
      mockApiFetch
        .mockResolvedValueOnce(createMockSuccessResponse(makeNationalOverview()))
        .mockResolvedValueOnce(createMockSuccessResponse(makeWilayaReport()))

      const wrapper = shallowMount(WilayaDetailPage)
      await flush()

      expect(mockApiFetch).toHaveBeenNthCalledWith(1, "/api/admin/overview", expect.anything())
      expect(mockApiFetch).toHaveBeenNthCalledWith(2, "/api/admin/wilaya/1", expect.anything())
      expect(wrapper.text()).toContain("Alger")
      const dairaList = wrapper.findComponent(DairaList)
      expect(dairaList.exists()).toBe(true)
      expect(dairaList.props("dairas")).toEqual(makeWilayaReport().dairas)
    })

    it("shows not-found when the slug matches no wilaya", async () => {
      mockRole("national_admin")
      routeState.params.wilayaName = "oran"
      mockApiFetch.mockResolvedValueOnce(createMockSuccessResponse(makeNationalOverview()))

      const wrapper = shallowMount(WilayaDetailPage)
      await flush()

      expect(wrapper.find(".admin-error").text()).toBe("admin.wilaya_not_found")
      expect(mockApiFetch).toHaveBeenCalledTimes(1)
    })
  })

  describe("wilaya_admin", () => {
    it("renders the report directly without a second request", async () => {
      mockRole("wilaya_admin")
      mockApiFetch.mockResolvedValueOnce(createMockSuccessResponse(makeWilayaReport()))

      const wrapper = shallowMount(WilayaDetailPage)
      await flush()

      expect(mockApiFetch).toHaveBeenCalledTimes(1)
      expect(wrapper.text()).toContain("Alger")
      const dairaList = wrapper.findComponent(DairaList)
      expect(dairaList.props("dairas")).toEqual(makeWilayaReport().dairas)
    })

    it("shows not-found when the slug does not match the admin's wilaya", async () => {
      mockRole("wilaya_admin")
      routeState.params.wilayaName = "oran"
      mockApiFetch.mockResolvedValueOnce(createMockSuccessResponse(makeWilayaReport()))

      const wrapper = shallowMount(WilayaDetailPage)
      await flush()

      expect(wrapper.find(".admin-error").text()).toBe("admin.wilaya_not_found")
    })
  })

  it("denies access for commune users", async () => {
    mockRole("commune_user")
    mockApiFetch.mockResolvedValueOnce(createMockSuccessResponse({}))

    const wrapper = shallowMount(WilayaDetailPage)
    await flush()

    expect(wrapper.find(".admin-error").text()).toBe("admin.access_denied")
  })

  it("shows a translated error when the request fails", async () => {
    mockRole("national_admin")
    mockApiFetch.mockRejectedValueOnce(new Error("boom"))

    const wrapper = shallowMount(WilayaDetailPage)
    await flush()

    expect(wrapper.find(".admin-error").text()).toBe("err_unknown")
  })

  it("navigates back to /admin", async () => {
    mockRole("national_admin")
    mockApiFetch
      .mockResolvedValueOnce(createMockSuccessResponse(makeNationalOverview()))
      .mockResolvedValueOnce(createMockSuccessResponse(makeWilayaReport()))

    const wrapper = shallowMount(WilayaDetailPage)
    await flush()
    await wrapper.find(".back-btn").trigger("click")

    expect(mockRouterPush).toHaveBeenCalledWith("/admin")
  })
})
