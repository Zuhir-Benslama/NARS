import { describe, it, expect } from "vitest"
import { mount } from "@vue/test-utils"
import StatPill from "./StatPill.vue"

describe("StatPill", () => {
  it("renders label and value", () => {
    const wrapper = mount(StatPill, { props: { label: "Areas", value: 5 } })
    expect(wrapper.text()).toContain("5")
    expect(wrapper.text()).toContain("Areas")
  })

  it("applies default class when no color prop", () => {
    const wrapper = mount(StatPill, { props: { label: "Test", value: 0 } })
    expect(wrapper.classes()).toContain("pill-default")
  })

  it("applies pill-blue class for blue color", () => {
    const wrapper = mount(StatPill, { props: { label: "Test", value: 0, color: "blue" } })
    expect(wrapper.classes()).toContain("pill-blue")
  })

  it("applies pill-green class for green color", () => {
    const wrapper = mount(StatPill, { props: { label: "Test", value: 0, color: "green" } })
    expect(wrapper.classes()).toContain("pill-green")
  })
})
