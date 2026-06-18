import { describe, it, expect, vi, beforeEach } from "vitest"
import { shallowMount } from "@vue/test-utils"
import { mockApiFetch, createMockSuccessResponse } from "../test/setup"
import AdminDashboard from "./AdminDashboard.vue"

const mockAppStore = vi.fn()
vi.mock("../stores/appStore", () => ({
  useAppStore: () => mockAppStore(),
}))

vi.mock("vue-i18n", () => ({
  useI18n: () => ({ t: (key: string) => key }),
}))

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
        wilaya_admin: { name: "Admin Alger" },
        dairas: [],
      },
      {
        wilaya_id: 2,
        wilaya_name_fr: "Oran",
        wilaya_name_ar: "وهران",
        daira_count: 9,
        commune_count: 26,
        commune_user_count: 55,
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
        daira_admin: { name: "Admin SM" },
        communes: [],
      },
    ],
  }
}

function makeDairaReport() {
  return {
    daira_id: 1,
    daira_name_fr: "Sidi M'Hamed",
    daira_name_ar: "سيدي امحمد",
    communes: [
      {
        commune_id: 1,
        commune_name_fr: "Alger Centre",
        commune_name_ar: "الجزائر الوسطى",
        user_count: 5,
      },
    ],
  }
}

function flush() {
  return new Promise((resolve) => setTimeout(resolve, 10))
}

describe("AdminDashboard", () => {
  beforeEach(() => {
    mockApiFetch.mockReset()
    mockApiFetch.mockResolvedValue(createMockSuccessResponse({}))
  })

  describe("header and common UI", () => {
    beforeEach(() => {
      // commune_user — no data-driven sections rendered
      mockAppStore.mockReturnValue({ user: { role: "commune_user" } })
    })

    it("renders the header with role badge", () => {
      const wrapper = shallowMount(AdminDashboard)
      expect(wrapper.find(".admin-title").exists()).toBe(true)
      expect(wrapper.find(".role-badge").exists()).toBe(true)
    })

    it("renders loading text while fetching", async () => {
      mockApiFetch.mockImplementationOnce(() => new Promise(() => {}))
      const wrapper = shallowMount(AdminDashboard)
      expect(wrapper.find(".admin-empty").exists()).toBe(true)
    })

    it("shows error message on API failure", async () => {
      mockApiFetch.mockResolvedValue({
        ok: false,
        status: 500,
        json: vi.fn().mockRejectedValue(new Error("Server error")),
      })
      const wrapper = shallowMount(AdminDashboard)
      await flush()
      expect(wrapper.find(".admin-error").exists()).toBe(true)
    })

    it("shows empty state when no data returned", async () => {
      mockApiFetch.mockResolvedValue(createMockSuccessResponse({}))
      const wrapper = shallowMount(AdminDashboard)
      await flush()
      expect(wrapper.find(".admin-empty").exists()).toBe(true)
    })
  })

  describe("national_admin view", () => {
    beforeEach(() => {
      mockAppStore.mockReturnValue({ user: { role: "national_admin" } })
      mockApiFetch.mockResolvedValue(createMockSuccessResponse(makeNationalOverview()))
    })

    it("renders wilaya cards grid", async () => {
      const wrapper = shallowMount(AdminDashboard)
      await flush()
      expect(wrapper.findAll(".wilaya-card").length).toBe(2)
    })

    it("shows wilaya names", async () => {
      const wrapper = shallowMount(AdminDashboard)
      await flush()
      expect(wrapper.text()).toContain("Alger")
      expect(wrapper.text()).toContain("Oran")
    })

    it("shows Arabic names", async () => {
      const wrapper = shallowMount(AdminDashboard)
      await flush()
      expect(wrapper.text()).toContain("الجزائر")
      expect(wrapper.text()).toContain("وهران")
    })

    it("shows admin name when assigned", async () => {
      const wrapper = shallowMount(AdminDashboard)
      await flush()
      expect(wrapper.text()).toContain("Admin Alger")
    })

    it("shows none_assigned when no admin", async () => {
      const wrapper = shallowMount(AdminDashboard)
      await flush()
      expect(wrapper.text()).toContain("none_assigned")
    })
  })

  describe("drill-down", () => {
    beforeEach(() => {
      mockAppStore.mockReturnValue({ user: { role: "national_admin" } })
      mockApiFetch.mockResolvedValue(createMockSuccessResponse(makeNationalOverview()))
    })

    it("renders router-link with correct slugified path", async () => {
      const wrapper = shallowMount(AdminDashboard)
      await flush()
      const links = wrapper.findAllComponents({ name: "RouterLinkStub" })
      expect(links.length).toBeGreaterThanOrEqual(2)
      expect(links[0].text()).toContain("Alger")
      expect(links[1].text()).toContain("Oran")
    })

    it("wilaya card has drill button", async () => {
      const wrapper = shallowMount(AdminDashboard)
      await flush()
      expect(wrapper.findAll(".wilaya-card").length).toBe(2)
      expect(wrapper.find(".drill-btn").exists()).toBe(true)
    })
  })

  describe("wilaya_admin view", () => {
    beforeEach(() => {
      mockAppStore.mockReturnValue({ user: { role: "wilaya_admin" } })
      mockApiFetch.mockResolvedValue(createMockSuccessResponse(makeWilayaReport()))
    })

    it("renders section title", async () => {
      const wrapper = shallowMount(AdminDashboard)
      await flush()
      expect(wrapper.find(".section-title").exists()).toBe(true)
    })
  })

  describe("daira_admin view", () => {
    beforeEach(() => {
      mockAppStore.mockReturnValue({ user: { role: "daira_admin" } })
      mockApiFetch.mockResolvedValue(createMockSuccessResponse(makeDairaReport()))
    })

    it("renders section title", async () => {
      const wrapper = shallowMount(AdminDashboard)
      await flush()
      expect(wrapper.find(".section-title").exists()).toBe(true)
    })
  })

  describe("role badge", () => {
    function mockForRole(role: string) {
      mockAppStore.mockReturnValue({ user: { role } })
      if (role === "national_admin") {
        mockApiFetch.mockResolvedValue(createMockSuccessResponse({ wilayas: [] }))
      } else if (role === "wilaya_admin") {
        mockApiFetch.mockResolvedValue(
          createMockSuccessResponse({ dairas: [], wilaya_name_fr: "", wilaya_name_ar: "" }),
        )
      } else if (role === "daira_admin") {
        mockApiFetch.mockResolvedValue(
          createMockSuccessResponse({ communes: [], daira_name_fr: "", daira_name_ar: "" }),
        )
      } else {
        mockApiFetch.mockResolvedValue(createMockSuccessResponse({}))
      }
    }

    it.each([
      ["national_admin", "badge-national"],
      ["wilaya_admin", "badge-wilaya"],
      ["daira_admin", "badge-daira"],
      ["commune_user", "badge-commune"],
      ["field_worker", "badge-commune"],
    ])("shows %s badge for %s role", async (role, expectedClass) => {
      mockForRole(role)
      const wrapper = shallowMount(AdminDashboard)
      await flush()
      expect(wrapper.find(".role-badge").classes()).toContain(expectedClass)
    })
  })

  describe("role label", () => {
    function mockForRole(role: string) {
      mockAppStore.mockReturnValue({ user: { role } })
      if (role === "national_admin") {
        mockApiFetch.mockResolvedValue(createMockSuccessResponse({ wilayas: [] }))
      } else if (role === "wilaya_admin") {
        mockApiFetch.mockResolvedValue(
          createMockSuccessResponse({ dairas: [], wilaya_name_fr: "", wilaya_name_ar: "" }),
        )
      } else if (role === "daira_admin") {
        mockApiFetch.mockResolvedValue(
          createMockSuccessResponse({ communes: [], daira_name_fr: "", daira_name_ar: "" }),
        )
      } else {
        mockApiFetch.mockResolvedValue(createMockSuccessResponse({}))
      }
    }

    it.each([
      ["national_admin", "admin.role.national"],
      ["wilaya_admin", "admin.role.wilaya"],
      ["daira_admin", "admin.role.daira"],
      ["commune_user", "admin.role.commune"],
      ["field_worker", "admin.role.field_worker"],
    ])("shows %s label for %s role", async (role, expectedKey) => {
      mockForRole(role)
      const wrapper = shallowMount(AdminDashboard)
      await flush()
      expect(wrapper.find(".role-badge").text()).toBe(expectedKey)
    })
  })
})
