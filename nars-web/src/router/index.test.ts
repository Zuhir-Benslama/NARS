import { describe, it, expect, beforeEach, vi } from "vitest"
import { setActivePinia, createPinia } from "pinia"
import type { UserInfo } from "../types"

describe("router", () => {
  it("creates a router instance with correct history mode", async () => {
    const { default: router } = await import("./index")
    expect(router).toBeDefined()
    expect(router.options.history).toBeDefined()
  })

  it("has expected routes", async () => {
    const { default: router } = await import("./index")
    const routes = router.getRoutes()
    const paths = routes.map((r) => r.path).sort()
    expect(paths).toContain("/")
    expect(paths).toContain("/admin")
    expect(paths).toContain("/map")
    expect(paths).toContain("/nars/:wilayaName")
  })

  it("route / has a redirect to /admin", async () => {
    const { default: router } = await import("./index")
    const routeRecord = router.options.routes.find((r) => r.path === "/")
    expect(routeRecord?.redirect).toBe("/admin")
  })

  it("route /map has a redirect to /", async () => {
    const { default: router } = await import("./index")
    const routeRecord = router.options.routes.find((r) => r.path === "/map")
    expect(routeRecord?.redirect).toBe("/")
  })
})

describe("router guards", () => {
  beforeEach(() => {
    vi.resetModules()
    setActivePinia(createPinia())
  })

  async function login(role: string): Promise<void> {
    const { useAppStore } = await import("../stores/appStore")
    useAppStore().setUser({ username: "user", role } as UserInfo)
  }

  it("redirects unauthenticated navigation to the login page", async () => {
    const assignMock = vi.fn()
    // jsdom does not implement window.location.assign; stub it and assert the call.
    Object.defineProperty(window, "location", {
      configurable: true,
      writable: true,
      value: { ...window.location, assign: assignMock },
    })
    const { default: router } = await import("./index")
    await router.push("/admin")
    expect(assignMock).toHaveBeenCalledWith("/login.html")
  })

  it("redirects unauthenticated root navigation to the login page", async () => {
    const assignMock = vi.fn()
    Object.defineProperty(window, "location", {
      configurable: true,
      writable: true,
      value: { ...window.location, assign: assignMock },
    })
    const { default: router } = await import("./index")
    await router.push("/")
    expect(assignMock).toHaveBeenCalledWith("/login.html")
  })

  it("allows authenticated admins to open /admin", async () => {
    await login("national_admin")
    const { default: router } = await import("./index")
    await router.push("/admin")
    expect(router.currentRoute.value.name).toBe("admin")
  })

  it("redirects / to /admin for authenticated admins", async () => {
    await login("wilaya_admin")
    const { default: router } = await import("./index")
    await router.push("/")
    expect(router.currentRoute.value.name).toBe("admin")
  })

  it("blocks non-admin users from /admin", async () => {
    await login("commune_user")
    const { default: router } = await import("./index")
    await router.push("/admin")
    expect(router.currentRoute.value.name).not.toBe("admin")
  })

  it("blocks non-admin users from wilaya detail pages", async () => {
    await login("field_worker")
    const { default: router } = await import("./index")
    await router.push("/nars/algiers")
    expect(router.currentRoute.value.name).not.toBe("wilaya-detail")
  })
})
