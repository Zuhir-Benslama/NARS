import { describe, it, expect, vi, beforeEach } from "vitest"
import { setActivePinia, createPinia } from "pinia"

const mockApiFetch = vi.fn()
vi.mock("../../api", () => ({
  apiFetch: mockApiFetch,
}))

let loadUserAndCommune: () => Promise<void>

beforeEach(async () => {
  vi.clearAllMocks()
  setActivePinia(createPinia())
  const mod = await import("./loader")
  loadUserAndCommune = mod.loadUserAndCommune
})

describe("loader", () => {
  it("re-exports loadFromDatabase", async () => {
    const mod = await import("./loader")
    expect(mod.loadFromDatabase).toBeDefined()
  })

  describe("loadUserAndCommune", () => {
    it("fetches current user and calls setUser", async () => {
      const userData = { id: 1, name: "Test", commune: { id: 42, name: "Testville" } }
      mockApiFetch.mockResolvedValue({
        json: () => Promise.resolve(userData),
      })

      await loadUserAndCommune()

      expect(mockApiFetch).toHaveBeenCalledWith("/api/current_user")
      const { useAppStore } = await import("../../stores/appStore")
      expect(useAppStore().user).toEqual(userData)
    })

    it("handles fetch errors gracefully", async () => {
      mockApiFetch.mockRejectedValue(new Error("Network failure"))

      await expect(loadUserAndCommune()).resolves.toBeUndefined()
    })
  })
})
