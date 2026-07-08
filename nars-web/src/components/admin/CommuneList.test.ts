import { describe, it, expect, vi } from "vitest"
import { mount } from "@vue/test-utils"

vi.mock("vue-i18n", () => ({
  useI18n: () => ({ t: (key: string) => key }),
}))

import CommuneList from "./CommuneList.vue"

const mockUser = (overrides: Record<string, any> = {}) => ({
  user_id: "1",
  username: "jdoe",
  name: "John Doe",
  email: "jdoe@test.com",
  role: "field_worker",
  areas: 5,
  districts: 3,
  city_centers: 1,
  roads: 12,
  house_entrances: 20,
  public_buildings: 2,
  public_spaces: 4,
  naming_panels: 1,
  total: 48,
  ...overrides,
})

describe("CommuneList", () => {
  it("renders commune name and user count", () => {
    const communes = [
      {
        commune_id: 1,
        commune_name_fr: "Algiers",
        commune_name_ar: "الجزائر",
        users: [mockUser()],
      },
    ]
    const wrapper = mount(CommuneList, { props: { communes } })
    expect(wrapper.text()).toContain("Algiers")
    expect(wrapper.text()).toContain("1 admin.users")
  })

  it("shows 'no users' when commune has no users", () => {
    const communes = [
      { commune_id: 2, commune_name_fr: "Oran", commune_name_ar: "وهران", users: [] },
    ]
    const wrapper = mount(CommuneList, { props: { communes } })
    expect(wrapper.text()).toContain("admin.no_users")
  })

  it("renders user stats in table", () => {
    const user = mockUser()
    const communes = [
      { commune_id: 1, commune_name_fr: "Test", commune_name_ar: "", users: [user] },
    ]
    const wrapper = mount(CommuneList, { props: { communes } })
    expect(wrapper.text()).toContain("jdoe")
    expect(wrapper.text()).toContain("5")
    expect(wrapper.text()).toContain("3")
    expect(wrapper.text()).toContain("12")
    expect(wrapper.text()).toContain("48")
  })

  it("shows FW badge for field_worker role", () => {
    const user = mockUser()
    const communes = [
      { commune_id: 1, commune_name_fr: "Test", commune_name_ar: "", users: [user] },
    ]
    const wrapper = mount(CommuneList, { props: { communes } })
    expect(wrapper.text()).toContain("FW")
  })

  it("hides FW badge for non-field-worker roles", () => {
    const user = mockUser({ role: "commune_admin" })
    const communes = [
      { commune_id: 1, commune_name_fr: "Test", commune_name_ar: "", users: [user] },
    ]
    const wrapper = mount(CommuneList, { props: { communes } })
    expect(wrapper.text()).not.toContain("FW")
  })

  it("renders totals row when multiple users", () => {
    const users = [
      mockUser({ user_id: 1, username: "a", areas: 2 }),
      mockUser({ user_id: 2, username: "b", areas: 3 }),
    ]
    const communes = [{ commune_id: 1, commune_name_fr: "Test", commune_name_ar: "", users }]
    const wrapper = mount(CommuneList, { props: { communes } })
    expect(wrapper.text()).toContain("admin.totals")
  })

  it("does not render totals row when single user", () => {
    const communes = [
      { commune_id: 1, commune_name_fr: "Test", commune_name_ar: "", users: [mockUser()] },
    ]
    const wrapper = mount(CommuneList, { props: { communes } })
    expect(wrapper.text()).not.toContain("admin.totals")
  })

  it("isFieldWorker returns true for field_worker", () => {
    const communes = [
      {
        commune_id: 1,
        commune_name_fr: "Test",
        commune_name_ar: "",
        users: [mockUser({ role: "field_worker" })],
      },
    ]
    const wrapper = mount(CommuneList, { props: { communes } })
    expect(wrapper.find(".fw-badge").exists()).toBe(true)
  })

  it("sum function produces correct totals", () => {
    const users = [
      mockUser({ user_id: 1, username: "a", areas: 2 }),
      mockUser({ user_id: 2, username: "b", areas: 3, roads: 5 }),
    ]
    const communes = [{ commune_id: 1, commune_name_fr: "Test", commune_name_ar: "", users }]
    const wrapper = mount(CommuneList, { props: { communes } })
    expect(wrapper.text()).toContain("5")
  })
})
