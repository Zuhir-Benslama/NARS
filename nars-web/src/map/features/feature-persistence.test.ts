import { describe, it, expect, vi, beforeEach } from "vitest"

const mockApiFetch = vi.fn()
vi.mock("../../api", () => ({
  apiFetch: mockApiFetch,
}))

let saveToDatabase: (featureData: any) => Promise<{ ok: boolean; data?: any; error?: string }>

async function reloadModule() {
  const mod = await import("./feature-persistence")
  saveToDatabase = mod.saveToDatabase
}

beforeEach(async () => {
  vi.clearAllMocks()
  await reloadModule()
})

describe("feature-persistence", () => {
  describe("saveToDatabase", () => {
    it("sends POST request and returns ok=true on success", async () => {
      mockApiFetch.mockResolvedValue({
        ok: true,
        json: () => Promise.resolve({ id: "new-42" }),
      })

      const result = await saveToDatabase({
        type: "areas",
        label: "My Area",
        decisionNumber: "123",
        decisionDate: "2024-01-01",
        areaTypeKey: "central_urban",
        coordinates: [{ lat: 36.0, lng: 127.0 }],
      })

      expect(result).toEqual({ ok: true, data: { id: "new-42" } })
      expect(mockApiFetch).toHaveBeenCalledWith("/api/features", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: expect.stringContaining("My Area"),
      })
    })

    it("returns ok=false on network error", async () => {
      mockApiFetch.mockRejectedValue(new Error("Network failure"))

      const result = await saveToDatabase({
        type: "areas",
        label: "A",
        decisionNumber: "",
        decisionDate: "",
      })

      expect(result.ok).toBe(false)
      expect(result.error).toBe("err_unknown")
    })

    it("includes shape type and layer in the body", async () => {
      mockApiFetch.mockResolvedValue({
        ok: true,
        json: () => Promise.resolve({ id: "new-99" }),
      })

      await saveToDatabase({
        type: "roads",
        label: "Main St",
        decisionNumber: "",
        decisionDate: "",
        roadTypeKey: "highway",
        coordinates: [{ lat: 36.0, lng: 127.0 }],
      })

      const body = JSON.parse(mockApiFetch.mock.calls[0][1].body)
      expect(body.type).toBe("road")
      expect(body.layer).toBe("highway")
      expect(body.label).toBe("Main St")
    })
  })
})
