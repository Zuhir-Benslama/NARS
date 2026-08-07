import { describe, it, expect, vi, beforeEach, afterEach } from "vitest"

let t: (key: string, replacements?: Record<string, string | number>) => string
let setLang: (lang: string) => Promise<void>
let applyInitialLang: () => Promise<void>
let currentLang: { value: string }

beforeEach(async () => {
  localStorage.clear()
  const mod = await vi.importActual<typeof import("./index")>("./index")
  t = mod.t
  setLang = mod.setLang
  applyInitialLang = mod.applyInitialLang
  currentLang = mod.currentLang
})

afterEach(() => {
  localStorage.clear()
})

describe("i18n", () => {
  describe("t()", () => {
    it("resolves known English keys from en.json", () => {
      const result = t("phase_areas_label")
      expect(result).toBeTruthy()
      expect(typeof result).toBe("string")
    })

    it("returns humanized fallback for unknown keys", () => {
      const result = t("hello_world")
      expect(result).toBe("World")
    })

    it("returns humanized fallback for single-word unknown keys", () => {
      const result = t("test")
      expect(result).toBe("Test")
    })

    it("handles phase keys with label/hint suffix stripping", () => {
      const result = t("phase_unknown_phase_label")
      expect(result).toBe("Unknown Phase")
    })

    it("humanizes keys where the first word is the prefix", () => {
      const result = t("not_a_real_key")
      expect(result).toBe("A Real Key")
    })

    it("handles decamelized keys via the body part", () => {
      const result = t("camelCase_key")
      expect(result).toBe("Key")
    })

    it("resolves every featureTypes key to a non-humanized string", () => {
      for (const key of [
        "central_urban",
        "secondary_urban",
        "housing_estate",
        "urban_pole",
        "district",
        "trad_activities_zone",
        "industry_zone",
        "boulevard",
        "avenue",
        "street",
        "drive",
        "lane",
        "cul_de_sac",
        "way",
        "garden",
        "square",
        "banking_postal",
        "commerce",
        "culture",
        "defence_security",
        "government_law",
        "healthcare",
        "higher_education",
        "national_education",
        "religious",
        "tourism",
        "transport",
        "vocational_training",
        "youth_sports",
        "bank",
        "post_office",
        "convention_centre",
        "public_market",
        "trade_centre",
        "library",
        "museum",
        "theater",
        "borders_guard",
        "customs",
        "fire_station",
        "gendarmes",
        "military_barrack",
        "police_station",
        "administrative_branch",
        "public_hospital",
        "neighborhood_health",
        "specialized_hospital",
        "treatment_room",
        "university_hospital",
        "research_institute",
        "university",
        "college",
        "school",
        "cemetery",
        "mosque",
        "hostel",
        "hotel",
        "motel",
        "airport",
        "bus_station",
        "train_station",
        "specialized_vocational_institute",
        "vocational_education_institute",
        "vocational_apprenticeship_center",
        "vocational_training_institute",
        "indoor_arena",
        "leisure_center",
        "sports_complex",
        "stadium",
        "swimming_pool",
        "youth_clubs",
        "youth_hostel",
      ]) {
        const result = t(`featureTypes.${key}`)
        expect(result).toBeTruthy()
        expect(result).not.toMatch(/^featureTypes/i)
      }
    })
  })

  describe("currentLang", () => {
    it("defaults to English", () => {
      expect(currentLang.value).toBe("en")
    })
  })

  describe("setLang()", () => {
    it("sets language to English and updates localStorage", async () => {
      await setLang("en")
      expect(currentLang.value).toBe("en")
      expect(localStorage.getItem("nars_lang")).toBe("en")
    })

    it("sets dir to ltr for English", async () => {
      await setLang("en")
      expect(document.documentElement.dir).toBe("ltr")
      expect(document.documentElement.lang).toBe("en")
    })

    it("sets dir to rtl for Arabic", async () => {
      await setLang("ar")
      expect(currentLang.value).toBe("ar")
      expect(document.documentElement.dir).toBe("rtl")
      expect(document.documentElement.lang).toBe("ar")
      expect(localStorage.getItem("nars_lang")).toBe("ar")
    })
  })

  describe("applyInitialLang()", () => {
    it("defaults to en when no localStorage value", async () => {
      await applyInitialLang()
      expect(currentLang.value).toBe("en")
    })

    it("reads existing localStorage value", async () => {
      localStorage.setItem("nars_lang", "fr")
      await applyInitialLang()
      expect(currentLang.value).toBe("fr")
    })
  })
})
