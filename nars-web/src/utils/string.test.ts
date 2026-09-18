import { describe, it, expect } from "vitest"
import { slugify } from "./string"

describe("slugify", () => {
  it("lowercases the input", () => {
    expect(slugify("Adrar")).toBe("adrar")
    expect(slugify("EL BAYADH")).toBe("el-bayadh")
  })

  it("strips combining diacritics from French wilaya names", () => {
    expect(slugify("Algérie")).toBe("algerie")
    expect(slugify("Boumerdès")).toBe("boumerdes")
    expect(slugify("Tébessa")).toBe("tebessa")
    expect(slugify("Aïn Témouchent")).toBe("ain-temouchent")
  })

  it("preserves Arabic characters", () => {
    expect(slugify("الجزائر")).toBe("الجزائر")
    expect(slugify("وهران")).toBe("وهران")
  })

  it("preserves Arabic hamza letterforms (أ إ ئ ؤ)", () => {
    expect(slugify("أدرار")).toBe("أدرار")
    expect(slugify("إليزي")).toBe("إليزي")
    expect(slugify("بجاية")).toBe("بجاية")
  })

  it("preserves CJK characters", () => {
    expect(slugify("北京市")).toBe("北京市")
  })

  it("converts whitespace runs to single hyphens", () => {
    expect(slugify("Béchar  Béni  Abbès")).toBe("bechar-beni-abbes")
    expect(slugify("  Tizi Ouzou  ")).toBe("tizi-ouzou")
  })

  it("collapses repeated hyphens", () => {
    expect(slugify("Sidi----Bel----Abbès")).toBe("sidi-bel-abbes")
  })

  it("drops characters outside the allowlist", () => {
    expect(slugify("Oran, Oran")).toBe("oran-oran")
    expect(slugify("Tipaza (Nord)")).toBe("tipaza-nord")
  })

  it("leaves an already-slugged name unchanged", () => {
    expect(slugify("Ain-El-Hammam")).toBe("ain-el-hammam")
  })

  it("handles empty and symbol-only input without throwing", () => {
    expect(slugify("")).toBe("")
    expect(slugify("!@#$%^&*()")).toBe("")
  })
})
