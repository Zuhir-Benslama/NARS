import type { Page } from "@playwright/test"

export function mockUserResponse(role = "commune_user") {
  return {
    id: 1,
    username: "testuser",
    role,
    commune: { id: 1, name_fr: "Alger Centre", name_ar: "الجزائر الوسطى" },
  }
}

export function mockLoadResponse() {
  return { features: [] }
}

export async function setupAuthMocks(page: Page, role = "commune_user") {
  const user = mockUserResponse(role)

  await page.route("**/api/current_user", async (route) => {
    const req = route.request()
    if (req.method() === "GET") {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(user),
      })
    } else {
      await route.fulfill({ status: 405 })
    }
  })

  await page.route("**/api/refresh", async (route) => {
    await route.fulfill({ status: 200 })
  })

  await page.route("**/api/load", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(mockLoadResponse()),
    })
  })

  await page.route("**/api/save", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ id: "mock-save-id" }),
    })
  })

  await page.route("**/api/update*", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ ok: true }),
    })
  })

  await page.route("**/api/delete*", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ ok: true }),
    })
  })

  await page.route("**/api/validate/**", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ valid: true }),
    })
  })

  await page.route("**/api/signin", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ ok: true }),
    })
  })

  await page.route("**/api/logout", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ ok: true }),
    })
  })
}
