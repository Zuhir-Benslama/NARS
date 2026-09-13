import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

// The global test setup (src/test/setup.ts) mocks ../api with a stub fetch.
// This file needs the REAL apiFetch so the drafts client's URL building and
// HTTP verbs can be asserted against global fetch.
vi.mock("../api", async (importOriginal) => {
  return await importOriginal<typeof import("../api")>()
})

import { acceptDraft, deleteDraft, listDrafts, rejectDraft, updateDraftGeometry } from "./drafts"

function okResponse(body: string): Response {
  return new Response(body, { status: 200, headers: { "Content-Type": "application/json" } })
}

function stubFetch(body: string) {
  const fetchMock = vi.fn((_url: string | URL | Request, _init?: RequestInit): Promise<Response> =>
    Promise.resolve(okResponse(body)),
  )
  vi.stubGlobal("fetch", fetchMock)
  return fetchMock
}

describe("drafts API client", () => {
  beforeEach(() => {
    // apiFetch logs errors via logError() (console.group/error) in dev — mute
    // the noise so assertions focus on behavior, not log output.
    vi.spyOn(console, "group").mockImplementation(() => {})
    vi.spyOn(console, "error").mockImplementation(() => {})
    vi.spyOn(console, "log").mockImplementation(() => {})
    vi.spyOn(console, "warn").mockImplementation(() => {})
  })

  afterEach(() => {
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
    vi.unstubAllEnvs()
  })

  it("listDrafts sends default paging and status params", async () => {
    const fetchMock = stubFetch(JSON.stringify({ items: [] }))

    const result = await listDrafts({ communeId: null })

    expect(result).toEqual([])
    const url = String(fetchMock.mock.calls[0][0])
    expect(url).toContain("/api/draft-features?")
    expect(url).toContain("status=pending")
    expect(url).toContain("skip=0")
    expect(url).toContain("take=200")
    expect(url).not.toContain("communeId")
  })

  it("listDrafts includes communeId, featureType and custom paging when provided", async () => {
    const fetchMock = stubFetch(JSON.stringify({ items: [] }))

    await listDrafts({ communeId: 7, featureType: "road", status: "rejected", skip: 25, take: 50 })

    const url = String(fetchMock.mock.calls[0][0])
    expect(url).toContain("communeId=7")
    expect(url).toContain("featureType=road")
    expect(url).toContain("status=rejected")
    expect(url).toContain("skip=25")
    expect(url).toContain("take=50")
  })

  it("listDrafts returns the paged items array", async () => {
    const items = [
      { id: "d1", featureType: "road", status: "pending", confidence: 0.9 },
      { id: "d2", featureType: "building", status: "pending", confidence: 0.8 },
    ]
    stubFetch(JSON.stringify({ items, total: 2 }))

    const result = await listDrafts({ communeId: null })

    expect(result).toHaveLength(2)
    expect(result[0]).toMatchObject({ id: "d1", featureType: "road" })
  })

  it("listDrafts tolerates a missing items field", async () => {
    stubFetch("{}")
    await expect(listDrafts({ communeId: null })).resolves.toEqual([])
  })

  it("acceptDraft POSTs to the accept endpoint", async () => {
    const fetchMock = stubFetch("")

    await acceptDraft("abc-123")

    expect(fetchMock).toHaveBeenCalledTimes(1)
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(String(url)).toContain("/api/draft-features/abc-123/accept")
    expect(init.method).toBe("POST")
  })

  it("rejectDraft POSTs to the reject endpoint", async () => {
    const fetchMock = stubFetch("")

    await rejectDraft("def-456")

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(String(url)).toContain("/api/draft-features/def-456/reject")
    expect(init.method).toBe("POST")
  })

  it("updateDraftGeometry PUTs the new GeoJSON as JSON", async () => {
    const fetchMock = stubFetch("")

    const geometry = '{"type":"LineString","coordinates":[[0,0],[1,1]]}'
    await updateDraftGeometry("ghi-789", geometry)

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(String(url)).toContain("/api/draft-features/ghi-789")
    expect(init.method).toBe("PUT")
    expect(init.body).toBe(JSON.stringify({ geometryGeoJson: geometry }))
    expect(String((init.headers as Record<string, string>)["Content-Type"])).toBe(
      "application/json",
    )
  })

  it("deleteDraft DELETEs the draft", async () => {
    const fetchMock = stubFetch("")

    await deleteDraft("jkl-000")

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(String(url)).toContain("/api/draft-features/jkl-000")
    expect(init.method).toBe("DELETE")
  })
})
