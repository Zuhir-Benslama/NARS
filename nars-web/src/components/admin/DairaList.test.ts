import { describe, it, expect, vi } from "vitest"
import { mount } from "@vue/test-utils"

vi.mock("vue-i18n", () => ({
  useI18n: () => ({ t: (key: string) => key }),
}))

import DairaList from "./DairaList.vue"

describe("DairaList", () => {
  const baseDaira = {
    daira_id: 1,
    daira_name_fr: "Sidi M'Hamed",
    daira_name_ar: "سيدي امحمد",
    communes: [],
    daira_admin: null,
  }

  it("renders daira name", () => {
    const wrapper = mount(DairaList, { props: { dairas: [baseDaira] } })
    expect(wrapper.text()).toContain("Sidi M'Hamed")
  })

  it("shows no-daira-admin badge when admin is null", () => {
    const wrapper = mount(DairaList, { props: { dairas: [baseDaira] } })
    expect(wrapper.text()).toContain("admin.no_daira_admin")
  })

  it("shows daira admin name when assigned", () => {
    const daira = {
      ...baseDaira,
      daira_admin: {
        user_id: "1",
        username: "admin",
        name: "Admin User",
        email: "admin@test.com",
        role: "daira_admin" as const,
      },
    }
    const wrapper = mount(DairaList, { props: { dairas: [daira] } })
    expect(wrapper.text()).toContain("Admin User")
  })

  it("shows commune count", () => {
    const commune = (id: number) => ({
      commune_id: id,
      commune_name_fr: `Commune ${id}`,
      commune_name_ar: "",
      users: [],
    })
    const daira = { ...baseDaira, communes: [commune(1), commune(2)] }
    const wrapper = mount(DairaList, { props: { dairas: [daira] } })
    expect(wrapper.text()).toContain("2 admin.communes")
  })
})
