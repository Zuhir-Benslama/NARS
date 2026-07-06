// ─── TEST SETUP ───────────────────────────────────────────────────────────────
// Global test utilities and mocks for Vitest.

import { config, RouterLinkStub } from "@vue/test-utils"
import { vi, beforeEach } from "vitest"
import { createPinia, setActivePinia } from "pinia"

// ─── PINIA SETUP ──────────────────────────────────────────────────────────────
// Required for testing stores outside of Vue components.

beforeEach(() => {
  setActivePinia(createPinia())
})

// Reset all module-level mutable state before each test to prevent state leakage
// across tests (draw-state, edit-state, snapping, undo, boundary, rotation, map).
// Uses dynamic import to avoid hoisting conflicts with vi.mock factories.
beforeEach(async () => {
  const { resetAllState } = await import("../map/reset-all-state")
  await resetAllState()
})

// ─── MOCK API FETCH ───────────────────────────────────────────────────────────

// Mock implementation of apiFetch for tests
export const mockApiFetch = vi.fn()

// Mock the apiFetch module
vi.mock("../api", () => ({
  apiUrl: (path: string) => path,
  apiFetch: mockApiFetch,
}))

// ─── MOCK I18N ────────────────────────────────────────────────────────────────

vi.mock("../i18n", () => ({
  i18n: {
    global: {
      locale: { value: "en" },
      t: (key: string) => key,
    },
  },
  t: (key: string) => key,
  setLang: vi.fn(),
  applyInitialLang: vi.fn(),
  currentLang: { value: "en" },
}))

// ─── MOCK MAPLIBRE ────────────────────────────────────────────────────────────

// Mock Maplibre GL JS
vi.mock("maplibre-gl", () => ({
  default: class MockMap {
    constructor() {
      this.sources = new Map()
      this.layers = new Map()
      this.handlers = new Map()
    }
    sources: Map<string, unknown>
    layers: Map<string, unknown>
    handlers: Map<string, unknown[]>

    on = vi.fn((event: string, handler: unknown) => {
      if (!this.handlers.has(event)) {
        this.handlers.set(event, [])
      }
      this.handlers.get(event)!.push(handler as never)
    })
    off = vi.fn()
    addSource = vi.fn((id: string, source: unknown) => {
      this.sources.set(id, source)
    })
    getSource = vi.fn((id: string) => this.sources.get(id))
    addLayer = vi.fn((layer: { id: string }) => {
      this.layers.set(layer.id, layer)
    })
    getStyle = vi.fn(() => ({ layers: [] }))
    queryRenderedFeatures = vi.fn(() => [])
    fitBounds = vi.fn()
    getCanvas = vi.fn(() => ({ style: { cursor: "" } }))
    once = vi.fn((_event: string, callback: () => void) => {
      setTimeout(callback, 0)
    })
  },
  Popup: class MockPopup {
    constructor() {}
    setLngLat = vi.fn(() => this)
    setHTML = vi.fn(() => this)
    addTo = vi.fn(() => this)
  },
  Marker: class MockMarker {
    constructor() {}
    setLngLat = vi.fn(() => this)
    addTo = vi.fn(() => this)
    remove = vi.fn()
  },
}))

// ─── HELPER FUNCTIONS ─────────────────────────────────────────────────────────

/**
 * Reset all mocks to their initial state.
 * Call this in beforeEach() to ensure clean test isolation.
 */
export function resetMocks(): void {
  mockApiFetch.mockReset()
  mockApiFetch.mockResolvedValue({
    ok: true,
    json: vi.fn().mockResolvedValue({}),
    text: vi.fn().mockResolvedValue(""),
  })
}

/**
 * Create a mock API response for a successful request.
 */
export function createMockSuccessResponse<T>(data: T): Partial<Response> {
  return {
    ok: true,
    status: 200,
    json: vi.fn().mockResolvedValue(data),
    text: vi.fn().mockResolvedValue(JSON.stringify(data)),
  }
}

/**
 * Create a mock API response for an error request.
 */
export function createMockErrorResponse(status: number, message: string): Partial<Response> {
  return {
    ok: false,
    status,
    json: vi.fn().mockRejectedValue(new Error(message)),
    text: vi.fn().mockResolvedValue(message),
  }
}

/**
 * Wait for the next tick (useful for async Vue component updates).
 */
export async function nextTick(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0))
}

// ─── GLOBAL CONFIG ────────────────────────────────────────────────────────────

// Configure Vue Test Utils globally
config.global.mocks = {
  $t: (key: string) => key,
}
config.global.stubs = {
  "router-link": RouterLinkStub,
  "router-view": true,
}

// Note: beforeEach is called by Vitest automatically via the test setup
