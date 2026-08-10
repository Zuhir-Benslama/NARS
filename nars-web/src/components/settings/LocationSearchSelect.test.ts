import { describe, it, expect, vi, beforeEach, afterEach } from "vitest"
import { mount } from "@vue/test-utils"
import { nextTick } from "vue"
import { mockApiFetch, createMockSuccessResponse } from "../../test/setup"

import LocationSearchSelect from "./LocationSearchSelect.vue"

function mountSelect(overrides: Record<string, unknown> = {}) {
  return mount(LocationSearchSelect, {
    props: {
      modelValue: null,
      label: "Wilaya",
      placeholder: "Search…",
      endpoint: "/api/wilayas",
      ...overrides,
    },
  })
}

function dropdownItems() {
  return Array.from(document.querySelectorAll(".lss-dropdown-item"))
}

describe("LocationSearchSelect", () => {
  beforeEach(() => {
    vi.useFakeTimers()
    mockApiFetch.mockReset()
  })

  afterEach(() => {
    vi.useRealTimers()
    document.body.innerHTML = ""
  })

  it("renders label, placeholder and disabled state", () => {
    const wrapper = mountSelect({ disabled: true })
    expect(wrapper.text()).toContain("Wilaya")
    const input = wrapper.find("input")
    expect(input.attributes("placeholder")).toBe("Search…")
    expect(input.attributes("disabled")).toBeDefined()
  })

  it("triggers a debounced search while typing", async () => {
    mockApiFetch.mockResolvedValue(createMockSuccessResponse({ items: [] }))
    const wrapper = mountSelect()
    await wrapper.find("input").setValue("al")
    expect(mockApiFetch).not.toHaveBeenCalled()
    await vi.advanceTimersByTimeAsync(250)
    expect(mockApiFetch).toHaveBeenCalledWith("/api/wilayas?search=al")
  })

  it("triggers a search on focus with an empty query", async () => {
    mockApiFetch.mockResolvedValue(createMockSuccessResponse({ items: [] }))
    const wrapper = mountSelect()
    await wrapper.find("input").trigger("focus")
    await vi.advanceTimersByTimeAsync(250)
    expect(mockApiFetch).toHaveBeenCalledWith("/api/wilayas?search=")
  })

  it("supports a function endpoint", async () => {
    mockApiFetch.mockResolvedValue(createMockSuccessResponse({ items: [] }))
    const wrapper = mountSelect({ endpoint: (q: string) => `/custom/${encodeURIComponent(q)}` })
    await wrapper.find("input").setValue("hi")
    await vi.advanceTimersByTimeAsync(250)
    expect(mockApiFetch).toHaveBeenCalledWith("/custom/hi")
  })

  it("extracts options from various payload key shapes", async () => {
    mockApiFetch.mockResolvedValue(
      createMockSuccessResponse({
        items: [
          { id: 1, name_fr: "Alger" },
          { id: 2, nameFr: "Oran" },
          { id: 3, name_ar: "الجزائر" },
          { id: 4, full_name: "Tlemcen" },
        ],
      }),
    )
    const wrapper = mountSelect()
    await wrapper.find("input").setValue("a")
    await vi.advanceTimersByTimeAsync(250)
    await nextTick()
    const items = dropdownItems()
    expect(items.map((i) => i.textContent)).toEqual(["Alger", "Oran", "الجزائر", "Tlemcen"])
  })

  it("filters out invalid options", async () => {
    mockApiFetch.mockResolvedValue(
      createMockSuccessResponse({
        items: [{ id: "x", name_fr: "Bad" }, null, { id: 5, name_fr: "Ok" }, { id: 6 }],
      }),
    )
    const wrapper = mountSelect()
    await wrapper.find("input").setValue("a")
    await vi.advanceTimersByTimeAsync(250)
    await nextTick()
    const items = dropdownItems()
    expect(items.length).toBe(1)
    expect(items[0].textContent).toBe("Ok")
  })

  it("selects an option and emits the id", async () => {
    mockApiFetch.mockResolvedValue(
      createMockSuccessResponse({ items: [{ id: 7, name_fr: "Alger" }] }),
    )
    const wrapper = mountSelect()
    await wrapper.find("input").setValue("al")
    await vi.advanceTimersByTimeAsync(250)
    await nextTick()
    dropdownItems()[0].dispatchEvent(new MouseEvent("click"))
    await nextTick()
    expect(wrapper.emitted("update:modelValue")![0]).toEqual([7])
    expect(dropdownItems().length).toBe(0)
    expect((wrapper.find("input").element as HTMLInputElement).value).toBe("Alger")
  })

  it("ignores a stale response that arrives after a newer query", async () => {
    let resolveFirst!: (value: Response) => void
    mockApiFetch
      .mockImplementationOnce(
        () =>
          new Promise((res) => {
            resolveFirst = res as (value: Response) => void
          }),
      )
      .mockResolvedValueOnce(createMockSuccessResponse({ items: [{ id: 2, name_fr: "FRESH" }] }))

    const wrapper = mountSelect()
    await wrapper.find("input").setValue("a")
    await vi.advanceTimersByTimeAsync(250)
    await wrapper.find("input").setValue("ab")
    await vi.advanceTimersByTimeAsync(250)
    await nextTick()

    resolveFirst(createMockSuccessResponse({ items: [{ id: 1, name_fr: "STALE" }] }) as Response)
    await nextTick()

    const items = dropdownItems()
    expect(items.length).toBe(1)
    expect(items[0].textContent).toContain("FRESH")
    expect(items[0].textContent).not.toContain("STALE")
  })

  it("clears pending timers on unmount", async () => {
    mockApiFetch.mockResolvedValue(createMockSuccessResponse({ items: [] }))
    const wrapper = mountSelect()
    await wrapper.find("input").setValue("al")
    wrapper.unmount()
    await vi.advanceTimersByTimeAsync(250)
    expect(mockApiFetch).not.toHaveBeenCalled()
  })
})
