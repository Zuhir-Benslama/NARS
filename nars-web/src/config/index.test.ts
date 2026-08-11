import { describe, it, expect } from "vitest"
import { getApiBaseUrl, getLoginPath, isDev } from "./index"

describe("config helpers", () => {
  describe("getApiBaseUrl", () => {
    it("returns API base URL", () => {
      const url = getApiBaseUrl()
      expect(typeof url).toBe("string")
    })
  })

  describe("getLoginPath", () => {
    it("returns path string", () => {
      const path = getLoginPath()
      expect(typeof path).toBe("string")
    })
  })

  describe("isDev", () => {
    it("returns true in vitest mode", () => {
      expect(isDev()).toBe(true)
    })
  })
})
