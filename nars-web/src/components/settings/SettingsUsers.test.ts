import { describe, it, expect, vi, beforeEach } from "vitest"
import { mount } from "@vue/test-utils"
import { mockApiFetch, createMockSuccessResponse } from "../../test/setup"
import SettingsUsers from "./SettingsUsers.vue"

const mockAppStore = vi.fn()
vi.mock("../../stores/appStore", () => ({
  useAppStore: () => mockAppStore(),
}))

vi.mock("vue-i18n", () => ({
  useI18n: () => ({ t: (key: string) => key }),
}))

vi.mock("../../lib/toast", () => ({
  showToast: vi.fn(),
}))

describe("SettingsUsers", () => {
  beforeEach(() => {
    mockAppStore.mockReturnValue({ user: { role: "wilaya_admin" } })
    mockApiFetch.mockReset()
    mockApiFetch.mockResolvedValue(createMockSuccessResponse({}))
  })

  describe("hint text", () => {
    it.each([
      ["national_admin", "su_hint_national"],
      ["wilaya_admin", "su_hint_wilaya"],
      ["daira_admin", "su_hint_daira"],
      ["commune_user", "su_hint_commune"],
    ])("shows %s hint for %s role", (role, expectedHint) => {
      mockAppStore.mockReturnValue({ user: { role } })
      const wrapper = mount(SettingsUsers)
      expect(wrapper.find(".settings-hint").text()).toBe(expectedHint)
    })
  })

  describe("role selector visibility", () => {
    it("hides role selector for national_admin (single target)", () => {
      mockAppStore.mockReturnValue({ user: { role: "national_admin" } })
      const wrapper = mount(SettingsUsers)
      expect(wrapper.find("select").exists()).toBe(false)
    })

    it("hides role selector for commune_user (single target)", () => {
      mockAppStore.mockReturnValue({ user: { role: "commune_user" } })
      const wrapper = mount(SettingsUsers)
      expect(wrapper.find("select").exists()).toBe(false)
    })
  })

  describe("location selector visibility", () => {
    it("shows wilaya selector for national_admin", () => {
      mockAppStore.mockReturnValue({ user: { role: "national_admin" } })
      const wrapper = mount(SettingsUsers)
      const labels = wrapper.findAll("label").map((l) => l.text())
      expect(labels).toContain("su_wilaya")
    })

    it("shows daira selector for wilaya_admin", () => {
      mockAppStore.mockReturnValue({ user: { role: "wilaya_admin" } })
      const wrapper = mount(SettingsUsers)
      const labels = wrapper.findAll("label").map((l) => l.text())
      expect(labels).toContain("su_daira")
    })

    it("shows commune selector for daira_admin", () => {
      mockAppStore.mockReturnValue({ user: { role: "daira_admin" } })
      const wrapper = mount(SettingsUsers)
      const labels = wrapper.findAll("label").map((l) => l.text())
      expect(labels).toContain("su_commune")
    })

    it("hides all selectors for commune_user creating field_worker", () => {
      mockAppStore.mockReturnValue({ user: { role: "commune_user" } })
      const wrapper = mount(SettingsUsers)
      const labels = wrapper.findAll("label").map((l) => l.text())
      expect(labels).not.toContain("su_wilaya")
      expect(labels).not.toContain("su_daira")
      expect(labels).not.toContain("su_commune")
    })
  })

  describe("validation", () => {
    async function expectValidationError(
      role: string,
      setup: (w: ReturnType<typeof mount>) => Promise<void>,
      expectedKey: string,
    ) {
      mockAppStore.mockReturnValue({ user: { role } })
      const wrapper = mount(SettingsUsers)
      if (setup) await setup(wrapper)
      await wrapper.find(".modal-btn-save").trigger("click")
      expect(wrapper.find(".su-error").text()).toBe(expectedKey)
    }

    it("requires name", async () => {
      await expectValidationError(
        "wilaya_admin",
        async (w) => {
          const inputs = w.findAll("input")
          await inputs[1].setValue("john@example.com")
          await inputs[2].setValue("+213-555-123456")
          await inputs[3].setValue("johndoe")
          await inputs[4].setValue("password123")
        },
        "su_err_name",
      )
    })

    it("requires valid email", async () => {
      await expectValidationError(
        "wilaya_admin",
        async (w) => {
          const inputs = w.findAll("input")
          await inputs[0].setValue("John Doe")
          await inputs[2].setValue("+213-555-123456")
          await inputs[3].setValue("johndoe")
          await inputs[4].setValue("password123")
        },
        "su_err_email",
      )
    })

    it("requires password >= 8 chars", async () => {
      await expectValidationError(
        "wilaya_admin",
        async (w) => {
          const inputs = w.findAll("input")
          await inputs[0].setValue("John Doe")
          await inputs[1].setValue("john@example.com")
          await inputs[2].setValue("+213-555-123456")
          await inputs[3].setValue("johndoe")
          await inputs[4].setValue("short")
        },
        "su_err_password",
      )
    })

    it("requires username >= 3 chars", async () => {
      await expectValidationError(
        "wilaya_admin",
        async (w) => {
          const inputs = w.findAll("input")
          await inputs[0].setValue("John Doe")
          await inputs[1].setValue("john@example.com")
          await inputs[2].setValue("+213-555-123456")
          await inputs[3].setValue("ab")
          await inputs[4].setValue("password123")
        },
        "su_err_username",
      )
    })

    it("requires wilaya for national_admin", async () => {
      await expectValidationError(
        "national_admin",
        async (w) => {
          const inputs = w.findAll("input")
          await inputs[0].setValue("John Doe")
          await inputs[1].setValue("john@example.com")
          await inputs[2].setValue("+213-555-123456")
          await inputs[3].setValue("johndoe")
          await inputs[4].setValue("password123")
        },
        "su_err_wilaya",
      )
    })

    it("requires daira for wilaya_admin", async () => {
      await expectValidationError(
        "wilaya_admin",
        async (w) => {
          const inputs = w.findAll("input")
          await inputs[0].setValue("John Doe")
          await inputs[1].setValue("john@example.com")
          await inputs[2].setValue("+213-555-123456")
          await inputs[3].setValue("johndoe")
          await inputs[4].setValue("password123")
        },
        "su_err_daira",
      )
    })
  })

  describe("submission", () => {
    it("creates user successfully for wilaya_admin", async () => {
      mockAppStore.mockReturnValue({ user: { role: "wilaya_admin" } })
      const wrapper = mount(SettingsUsers)
      const inputs = wrapper.findAll("input")
      await inputs[0].setValue("John Doe")
      await inputs[1].setValue("john@example.com")
      await inputs[2].setValue("+213-555-123456")
      await inputs[3].setValue("johndoe")
      await inputs[4].setValue("password123")

      // Select a daira by directly setting state
      ;(wrapper.vm as any).selectedDairaId = 1

      await wrapper.find(".modal-btn-save").trigger("click")
      await new Promise((resolve) => setTimeout(resolve, 0))

      expect(mockApiFetch).toHaveBeenCalledWith(
        "/api/admin/users",
        expect.objectContaining({
          method: "POST",
          body: expect.stringContaining("john@example.com"),
        }),
      )
    })

    it("shows success message on creation", async () => {
      mockAppStore.mockReturnValue({ user: { role: "commune_user" } })
      mockApiFetch.mockResolvedValue(createMockSuccessResponse({}))
      const wrapper = mount(SettingsUsers)
      const inputs = wrapper.findAll("input")
      await inputs[0].setValue("John Doe")
      await inputs[1].setValue("john@example.com")
      await inputs[2].setValue("+213-555-123456")
      await inputs[3].setValue("johndoe")
      await inputs[4].setValue("password123")

      await wrapper.find(".modal-btn-save").trigger("click")
      await new Promise((resolve) => setTimeout(resolve, 0))

      expect(wrapper.find(".su-success").exists()).toBe(true)
    })

    it("shows error on API failure", async () => {
      mockAppStore.mockReturnValue({ user: { role: "commune_user" } })
      mockApiFetch.mockResolvedValueOnce(createMockSuccessResponse([])).mockResolvedValueOnce({
        ok: false,
        json: vi.fn().mockResolvedValue({ detail: "Email already exists" }),
      })
      const wrapper = mount(SettingsUsers)
      await new Promise((resolve) => setTimeout(resolve, 0))

      const inputs = wrapper.findAll("input")
      await inputs[0].setValue("John Doe")
      await inputs[1].setValue("john@example.com")
      await inputs[2].setValue("+213-555-123456")
      await inputs[3].setValue("johndoe")
      await inputs[4].setValue("password123")

      await wrapper.find(".modal-btn-save").trigger("click")
      await new Promise((resolve) => setTimeout(resolve, 0))

      expect(wrapper.find(".su-error").text()).toBe("Email already exists")
    })

    it("resets form after successful creation", async () => {
      mockAppStore.mockReturnValue({ user: { role: "commune_user" } })
      const wrapper = mount(SettingsUsers)
      const inputs = wrapper.findAll("input")
      await inputs[0].setValue("John Doe")
      await inputs[1].setValue("john@example.com")
      await inputs[2].setValue("+213-555-123456")
      await inputs[3].setValue("johndoe")
      await inputs[4].setValue("password123")

      await wrapper.find(".modal-btn-save").trigger("click")
      await new Promise((resolve) => setTimeout(resolve, 0))

      const nameInput = wrapper.findAll("input")[0].element as HTMLInputElement
      expect(nameInput.value).toBe("")
    })
  })

  describe("role options for national_admin", () => {
    it("defaults target to wilaya_admin (single target)", () => {
      mockAppStore.mockReturnValue({ user: { role: "national_admin" } })
      const wrapper = mount(SettingsUsers)
      expect((wrapper.vm as any).targetRole).toBe("wilaya_admin")
    })
  })

  describe("daira selector interaction", () => {
    it("sets selectedDairaId on v-model change", async () => {
      mockAppStore.mockReturnValue({ user: { role: "wilaya_admin" } })
      const wrapper = mount(SettingsUsers)
      ;(wrapper.vm as any).selectedDairaId = 5
      await new Promise((resolve) => setTimeout(resolve, 0))

      expect((wrapper.vm as any).selectedDairaId).toBe(5)
    })

    it("cascades daira change to reset commune", async () => {
      mockAppStore.mockReturnValue({ user: { role: "wilaya_admin" } })
      const wrapper = mount(SettingsUsers)
      ;(wrapper.vm as any).selectedDairaId = 5
      ;(wrapper.vm as any).selectedCommuneId = 10
      await new Promise((resolve) => setTimeout(resolve, 0))

      expect((wrapper.vm as any).selectedCommuneId).toBeNull()
    })
  })
})
