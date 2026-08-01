import { test, expect, type Page } from "@playwright/test"
import { setupAuthMocks } from "./test-data"

function isApiRequest(url: string): boolean {
  try {
    return new URL(url).pathname.startsWith("/api/")
  } catch {
    return false
  }
}

async function mockSignin(page: Page, status: number, body: Record<string, unknown>) {
  await page.route("**/api/signin", async (route) => {
    if (!isApiRequest(route.request().url())) {
      await route.continue()
      return
    }
    await route.fulfill({
      status,
      contentType: "application/json",
      body: JSON.stringify(body),
    })
  })
}

async function submitCredentials(page: Page, username: string, password: string) {
  await page.fill("#signin-username", username)
  await page.fill("#signin-password", password)
  await page.click("#signinForm .btn")
}

test.describe("Auth flows — static login page", () => {
  test("successful sign-in redirects to the map and boots the SPA", async ({ page }) => {
    await setupAuthMocks(page, "commune_user")
    await page.goto("/login.html")
    await page.waitForLoadState("load")

    await submitCredentials(page, "testuser", "correct-password")

    await expect(page).not.toHaveURL(/login\.html/, { timeout: 10000 })
    await expect(page.locator("#phaseBar")).toBeVisible({ timeout: 20000 })
  })

  test("invalid credentials show the error message without redirecting", async ({ page }) => {
    await mockSignin(page, 401, { detail: "Invalid username or password" })
    await page.goto("/login.html")
    await page.waitForLoadState("load")

    await submitCredentials(page, "testuser", "wrong-password")

    const error = page.locator("#signinError")
    await expect(error).toBeVisible({ timeout: 5000 })
    await expect(error).toContainText("Invalid username or password")
    await expect(page).toHaveURL(/login\.html/)
  })

  test("submitting an empty form is blocked by client-side validation", async ({ page }) => {
    let signinCalls = 0
    await page.route("**/api/signin", async (route) => {
      if (!isApiRequest(route.request().url())) {
        await route.continue()
        return
      }
      signinCalls++
      await route.fulfill({ status: 200, contentType: "application/json", body: "{}" })
    })
    await page.goto("/login.html")
    await page.waitForLoadState("load")

    await page.click("#signinForm .btn")
    await page.waitForTimeout(500)

    expect(signinCalls).toBe(0)
    await expect(page.locator("#signinError")).not.toBeVisible()
    await expect(page).toHaveURL(/login\.html/)
  })

  test("network failure surfaces the server error message", async ({ page }) => {
    await page.route("**/api/signin", async (route) => {
      if (!isApiRequest(route.request().url())) {
        await route.continue()
        return
      }
      await route.abort()
    })
    await page.goto("/login.html")
    await page.waitForLoadState("load")

    await submitCredentials(page, "testuser", "any-password")

    const error = page.locator("#signinError")
    await expect(error).toBeVisible({ timeout: 5000 })
    await expect(error).toContainText("Error connecting to server")
  })

  test("language switcher translates the form and toggles RTL for Arabic", async ({ page }) => {
    await page.goto("/login.html")
    await page.waitForLoadState("load")

    await page.selectOption("#langSelect", "fr")
    await expect(page.locator("#signinForm .btn")).toHaveText("Se connecter")

    await page.selectOption("#langSelect", "ar")
    await expect(page.locator("#signinForm .btn")).toHaveText("دخول")
    await expect(page.locator("html")).toHaveAttribute("dir", "rtl")
    await expect(page.locator("html")).toHaveAttribute("lang", "ar")

    const stored = await page.evaluate(() => localStorage.getItem("nars_lang"))
    expect(stored).toBe("ar")
  })

  test("theme switcher applies and persists the selection", async ({ page }) => {
    await page.goto("/login.html")
    await page.waitForLoadState("load")

    await page.selectOption("#themeSelect", "dark")
    await expect(page.locator("html")).toHaveAttribute("data-theme", "dark")

    const stored = await page.evaluate(() => localStorage.getItem("nars_theme"))
    expect(stored).toBe("dark")

    // Reload — the persisted theme is re-applied on boot
    await page.reload()
    await expect(page.locator("html")).toHaveAttribute("data-theme", "dark")
  })
})
