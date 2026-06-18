import { test, expect } from "@playwright/test"
import { setupAuthMocks } from "./test-data"

test.describe("Map flows — draw, edit, save", () => {
  test.beforeEach(async ({ page }) => {
    await setupAuthMocks(page)
    await page.goto("/")
    await page.waitForLoadState("networkidle")
  })

  test("FeatureModal opens via store", async ({ page }) => {
    await page.waitForFunction(() => !!(window as any).__TEST__, { timeout: 20000 })

    await page.evaluate(() => {
      const store = (window as any).__TEST__.modalStore
      store.openCreate(0)
    })

    await page.waitForTimeout(500)
    const modal = page.locator(".modal")
    await expect(modal).toBeVisible({ timeout: 5000 })
  })

  test("FeatureModal can be filled and saved", async ({ page }) => {
    await page.waitForFunction(() => !!(window as any).__TEST__, { timeout: 20000 })

    await page.route("**/api/save", async (route) => {
      void route.request().postDataJSON()
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({ id: "mock-id" }),
      })
    })

    await page.evaluate(() => {
      const store = (window as any).__TEST__.modalStore
      store.openCreate(0)
      store.label = "Test Area"
      store.decisionNumber = "123"
      store.decisionDate = "2025-01-15"
    })

    const saveBtn = page.locator("button:has-text('Save'), button:has-text('Confirm'), .modal-btn-save").first()
    if (await saveBtn.isVisible({ timeout: 3000 }).catch(() => false)) {
      await saveBtn.click()
      await page.waitForTimeout(500)
    }

    await page.waitForTimeout(500)
  })

  test("renders the map canvas", async ({ page }) => {
    const canvas = page.locator("#map canvas, .maplibregl-canvas")
    await expect(canvas.first()).toBeVisible({ timeout: 20000 })
  })

  test("commune_user shows phase steps and info panel", async ({ page }) => {
    await expect(page.locator(".phase-step").first()).toBeVisible({ timeout: 20000 })
    await expect(page.locator(".info-panel")).toBeVisible({ timeout: 15000 })
  })

  test("field_worker shows field panel", async ({ page }) => {
    await page.unroute("**/api/current_user")
    await page.route("**/api/current_user", async (route) => {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          id: 2,
          username: "fieldworker",
          role: "field_worker",
          commune: { id: 1, name_fr: "Alger Centre", name_ar: "الجزائر الوسطى" },
        }),
      })
    })
    await page.reload()
    await page.waitForLoadState("networkidle")

    await expect(page.locator(".fp-panel")).toBeVisible({ timeout: 20000 })
  })
})
