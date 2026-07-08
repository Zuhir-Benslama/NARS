import { describe, it, expect } from "vitest"

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
