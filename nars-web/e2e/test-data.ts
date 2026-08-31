import type { Page } from "@playwright/test"

export function mockUserResponse(role = "commune_user") {
  return {
    id: 1,
    username: "testuser",
    role,
    commune: { id: 1, name_fr: "Alger Centre", name_ar: "الجزائر الوسطى" },
  }
}

export const MOCK_BOUNDARY = {
  type: "Polygon",
  coordinates: [
    [
      [2.5, 36.5],
      [2.8, 36.5],
      [2.8, 36.8],
      [2.5, 36.8],
      [2.5, 36.5],
    ],
  ],
}

async function fulfillJson(route: { fulfill: (opts: object) => Promise<unknown> }, body: unknown) {
  await route.fulfill({
    status: 200,
    contentType: "application/json",
    body: JSON.stringify(body),
  })
}

export async function setupAuthMocks(page: Page, role = "commune_user") {
  const user = mockUserResponse(role)

  // Only intercept requests whose pathname starts with /api/.
  // Without this check, globs like **/api/** also match Vite source
  // files at /src/api/index.ts, causing MIME-type errors.
  function isApiRequest(url: string): boolean {
    try {
      return new URL(url).pathname.startsWith("/api/")
    } catch {
      return false
    }
  }

  await page.route("**/api/current_user", async (route) => {
    if (!isApiRequest(route.request().url())) {
      await route.continue()
      return
    }
    if (route.request().method() === "GET") {
      await fulfillJson(route, user)
    } else {
      await route.fulfill({ status: 405 })
    }
  })

  await page.route("**/api/refresh", async (route) => {
    if (!isApiRequest(route.request().url())) {
      await route.continue()
      return
    }
    await fulfillJson(route, { ok: true })
  })

  await page.route("**/api/signin", async (route) => {
    if (!isApiRequest(route.request().url())) {
      await route.continue()
      return
    }
    await fulfillJson(route, { ok: true })
  })

  await page.route("**/api/logout", async (route) => {
    if (!isApiRequest(route.request().url())) {
      await route.continue()
      return
    }
    await fulfillJson(route, { ok: true })
  })

  // ── Features CRUD ──────────────────────────────────────────────────────
  await page.route("**/api/features**", async (route) => {
    if (!isApiRequest(route.request().url())) {
      await route.continue()
      return
    }
    const method = route.request().method()
    if (method === "GET") {
      await fulfillJson(route, [])
    } else if (method === "POST") {
      await route.fulfill({
        status: 201,
        contentType: "application/json",
        body: JSON.stringify({ id: "mock-id" }),
      })
    } else if (method === "PUT") {
      await fulfillJson(route, { id: "mock-id" })
    } else if (method === "DELETE") {
      await route.fulfill({ status: 204 })
    } else {
      await route.fulfill({ status: 405 })
    }
  })

  // ── Commune boundary ───────────────────────────────────────────────────
  await page.route("**/api/commune/**/boundary", async (route) => {
    if (!isApiRequest(route.request().url())) {
      await route.continue()
      return
    }
    await fulfillJson(route, { geometry: MOCK_BOUNDARY, commune_name: "Alger Centre" })
  })

  // ── Feature types ──────────────────────────────────────────────────────
  await page.route("**/api/feature-types**", async (route) => {
    if (!isApiRequest(route.request().url())) {
      await route.continue()
      return
    }
    await fulfillJson(route, [])
  })

  // ── Admin / user endpoints ─────────────────────────────────────────────
  await page.route("**/api/admin/**", async (route) => {
    if (!isApiRequest(route.request().url())) {
      await route.continue()
      return
    }
    const method = route.request().method()
    if (method === "DELETE") {
      await route.fulfill({ status: 204 })
    } else {
      await fulfillJson(route, [])
    }
  })

  await page.route("**/api/user/profile", async (route) => {
    if (!isApiRequest(route.request().url())) {
      await route.continue()
      return
    }
    await fulfillJson(route, { ok: true })
  })

  // ── Location endpoints ─────────────────────────────────────────────────
  await page.route("**/api/wilayas**", async (route) => {
    if (!isApiRequest(route.request().url())) {
      await route.continue()
      return
    }
    await fulfillJson(route, [])
  })
  await page.route("**/api/dairas**", async (route) => {
    if (!isApiRequest(route.request().url())) {
      await route.continue()
      return
    }
    await fulfillJson(route, [])
  })
  await page.route("**/api/communes**", async (route) => {
    if (!isApiRequest(route.request().url())) {
      await route.continue()
      return
    }
    await fulfillJson(route, [])
  })

  // ── Validation endpoints ───────────────────────────────────────────────
  await page.route("**/api/validate/**", async (route) => {
    if (!isApiRequest(route.request().url())) {
      await route.continue()
      return
    }
    await fulfillJson(route, { valid: true })
  })

  await page.route("**/api/road-side", async (route) => {
    if (!isApiRequest(route.request().url())) {
      await route.continue()
      return
    }
    await fulfillJson(route, { side: "right" })
  })

  // ── Field endpoints ────────────────────────────────────────────────────
  await page.route("**/api/field/**", async (route) => {
    if (!isApiRequest(route.request().url())) {
      await route.continue()
      return
    }
    await fulfillJson(route, { ok: true })
  })

  // ── Spatial / areas ────────────────────────────────────────────────────
  await page.route("**/api/areas/**", async (route) => {
    if (!isApiRequest(route.request().url())) {
      await route.continue()
      return
    }
    await fulfillJson(route, { ok: true })
  })

  // ── Logging (fire-and-forget) ──────────────────────────────────────────
  await page.route("**/api/logs", async (route) => {
    if (!isApiRequest(route.request().url())) {
      await route.continue()
      return
    }
    await route.fulfill({ status: 204 })
  })

  // ── Catch-all: prevent any unmocked /api/* from blocking networkidle ──
  await page.route("**/api/**", async (route) => {
    if (!isApiRequest(route.request().url())) {
      await route.continue()
      return
    }
    await fulfillJson(route, {})
  })
}
