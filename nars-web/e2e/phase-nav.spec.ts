import { test, expect } from "@playwright/test"
import { setupAuthMocks } from "./test-data"

test.describe("Phase Navigation", () => {
  test.beforeEach(async ({ page }) => {
    await setupAuthMocks(page)
    await page.goto("/")
    await page.waitForLoadState("load")
  })

  test("renders PhaseBar with all 8 phases", async ({ page }) => {
    const phaseBar = page.locator("#phaseBar")
    await expect(phaseBar).toBeVisible({ timeout: 20000 })

    const phaseSteps = phaseBar.locator(".phase-step")
    const count = await phaseSteps.count()
    expect(count).toBe(8)
  })

  test("first phase (areas) is active on load", async ({ page }) => {
    const firstStep = page.locator(".phase-step").first()
    await expect(firstStep).toBeVisible({ timeout: 20000 })
    await expect(firstStep).toHaveClass(/active/)
  })

  test("clicking a phase updates the active phase", async ({ page }) => {
    await expect(page.locator("#phaseBar")).toBeVisible({ timeout: 20000 })
    const phases = page.locator(".phase-step")
    const count = await phases.count()
    expect(count).toBeGreaterThanOrEqual(2)

    // Seed an area so the phase can advance (navigatePhase requires >0 areas)
    await page.evaluate(() => {
      const store = (window as unknown as { __TEST__?: { layerStore: { addFeature: Function } } }).__TEST__
      store?.layerStore.addFeature("areas", {
        id: "e2e-area-1",
        dbId: "e2e-area-1",
        type: "polygon",
        data: {
          type: "areas",
          label: "E2E Area",
          decisionNumber: "001",
          decisionDate: "2024-01-01",
          areaTypeKey: "central_urban",
        },
      })
    })

    await phases.nth(1).click()
    // Wait for Vue reactivity to settle after click
    await expect(page.locator(".phase-step").nth(1)).toHaveClass(/active/, { timeout: 5000 })
  })

  test("loads without console errors", async ({ page }) => {
    const errors: string[] = []
    page.on("pageerror", (err) => errors.push(err.message))

    await page.goto("/")
    await page.waitForLoadState("load")
    // Wait for async initialization (phase bar rendering) before collecting errors
    await expect(page.locator("#phaseBar")).toBeVisible({ timeout: 20000 })

    const filtered = errors.filter(
      (e) => !e.includes("manifest") && !e.includes("favicon") && !e.includes("ECONNREFUSED"),
    )
    expect(filtered).toEqual([])
  })

  test("shows InfoPanel with feature counts", async ({ page }) => {
    const infoPanel = page.locator(".info-panel")
    await expect(infoPanel).toBeVisible({ timeout: 20000 })
    await expect(infoPanel.locator(".info-title")).toBeVisible()
  })
})
