import { describe, it, expect, vi, beforeEach } from "vitest"
import { setActivePinia, createPinia } from "pinia"
import { useAppStore } from "../../stores/appStore"
import { useModalStore } from "../../stores/modalStore"
import { PHASES } from "../../phases"

const mockCheckMainUrbanExists = vi.hoisted(() => vi.fn().mockResolvedValue(false))

vi.mock("../../lib/validation", () => ({ checkMainUrbanExists: mockCheckMainUrbanExists }))

let mod: typeof import("./feature-modal")

function setUpUser(communeName: string | null) {
  const appStore = useAppStore()
  appStore.setUser({
    id: 1,
    username: "u",
    name: "U",
    email: "u@u.com",
    role: "commune_user",
    commune: { id: 99, name_fr: communeName ?? "C", name_ar: "", latitude: null, longitude: null },
  })
}

beforeEach(async () => {
  vi.clearAllMocks()
  setActivePinia(createPinia())
  mod = await import("./feature-modal")
  mockCheckMainUrbanExists.mockResolvedValue(false)
})

describe("prepareModalExtras", () => {
  it("does nothing for non-areas phases", async () => {
    const modalStore = useModalStore()
    const patchSpy = vi.spyOn(modalStore, "patchFields")
    await mod.prepareModalExtras(PHASES[1])
    expect(mockCheckMainUrbanExists).not.toHaveBeenCalled()
    expect(patchSpy).not.toHaveBeenCalled()
  })

  it("sets central_urban + commune name when no main urban area exists", async () => {
    const modalStore = useModalStore()
    mockCheckMainUrbanExists.mockResolvedValue(false)
    setUpUser("Alger")
    await mod.prepareModalExtras(PHASES[0])
    expect(modalStore.$state.mainUrbanExists).toBe(false)
    expect(modalStore.$state.areaTypeKey).toBe("central_urban")
    expect(modalStore.$state.label).toBe("Alger")
  })

  it("sets secondary_urban + empty label when a main urban area exists", async () => {
    const modalStore = useModalStore()
    mockCheckMainUrbanExists.mockResolvedValue(true)
    setUpUser("Alger")
    await mod.prepareModalExtras(PHASES[0])
    expect(modalStore.$state.mainUrbanExists).toBe(true)
    expect(modalStore.$state.areaTypeKey).toBe("secondary_urban")
    expect(modalStore.$state.label).toBe("")
  })
})
