import { describe, it, expect, vi, beforeEach } from "vitest"

const mocks = vi.hoisted(() => ({
  html2canvas: vi.fn(),
  jsPDF: vi.fn(),
  depsAvailable: { value: true },
}))

vi.mock("html2canvas", () => ({
  get default() {
    return mocks.depsAvailable.value ? mocks.html2canvas : undefined
  },
}))
vi.mock("jspdf", () => ({ default: mocks.jsPDF }))

import { exportMapToPdf } from "./export"

function makeCanvas(width = 1000, height = 800) {
  return {
    width,
    height,
    toDataURL: vi.fn(() => "data:image/jpeg;base64,AAAA"),
  }
}

function makeJsPDFInstance() {
  return {
    save: vi.fn(),
    addImage: vi.fn(),
    setFillColor: vi.fn(),
    rect: vi.fn(),
    setTextColor: vi.fn(),
    setFontSize: vi.fn(),
    setFont: vi.fn(),
    text: vi.fn(),
  }
}

function flush() {
  return new Promise((resolve) => setTimeout(resolve, 0))
}

describe("exportMapToPdf", () => {
  let pdfInstance: ReturnType<typeof makeJsPDFInstance>

  beforeEach(() => {
    vi.clearAllMocks()
    mocks.depsAvailable.value = true
    document.body.innerHTML = '<div id="map"></div>'
    mocks.html2canvas.mockResolvedValue(makeCanvas())
    pdfInstance = makeJsPDFInstance()
    // Regular function so `new jsPDF()` (Reflect.construct on the mock) works.
    mocks.jsPDF.mockImplementation(function () {
      return pdfInstance
    })
  })

  it("throws when the map element is missing", async () => {
    document.body.innerHTML = ""
    await expect(exportMapToPdf("A3", "Alger")).rejects.toThrow("Map element not found")
  })

  it("renders the map and produces a PDF file", async () => {
    const onProgress = vi.fn()

    await exportMapToPdf("A3", "Alger", onProgress)

    expect(mocks.html2canvas).toHaveBeenCalledWith(
      document.getElementById("map"),
      expect.objectContaining({ scale: 2, backgroundColor: "#000" }),
    )
    expect(mocks.jsPDF).toHaveBeenCalledWith(
      expect.objectContaining({ orientation: "landscape", unit: "mm", format: [420, 297] }),
    )
    expect(pdfInstance.addImage).toHaveBeenCalledTimes(1)
    expect(pdfInstance.save).toHaveBeenCalledWith(expect.stringMatching(/^NARS_Alger_A3_\d+\.pdf$/))
    expect(onProgress).toHaveBeenLastCalledWith(100, "export_step_done")
  })

  it("uses A0 scale and A0 page size for A0 exports", async () => {
    await exportMapToPdf("A0", "Alger")

    expect(mocks.html2canvas).toHaveBeenCalledWith(
      expect.anything(),
      expect.objectContaining({ scale: 3 }),
    )
    expect(mocks.jsPDF).toHaveBeenCalledWith(expect.objectContaining({ format: [1189, 841] }))
  })

  it("sizes the image to fit the printable area (landscape fit-by-height)", async () => {
    await exportMapToPdf("A3", "Sidi M'Hamed")

    // A3 = 420x297mm, margin 10, title bar offset 14
    // printW = 400, printH = 263; canvas aspect 1.25 < print aspect 1.52
    // => fit by height: imgH = 263, imgW = 263 * 1.25 = 328.75, centered
    expect(pdfInstance.addImage).toHaveBeenCalledWith(
      "data:image/jpeg;base64,AAAA",
      "JPEG",
      45.625,
      10,
      328.75,
      263,
    )
  })

  it("rejects and propagates the error when canvas rendering fails", async () => {
    mocks.html2canvas.mockRejectedValue(new Error("canvas exploded"))
    await expect(exportMapToPdf("A3", "Alger")).rejects.toThrow("canvas exploded")
    expect(mocks.jsPDF).not.toHaveBeenCalled()
  })

  it("rejects when the PDF dependencies are unavailable", async () => {
    mocks.depsAvailable.value = false
    await expect(exportMapToPdf("A3", "Alger")).rejects.toThrow(/html2canvas and jspdf/)
  })

  it("calls onProgress through the export pipeline", async () => {
    const onProgress = vi.fn()
    await exportMapToPdf("A3", "Alger", onProgress)
    await flush()
    const labels = onProgress.mock.calls.map((c) => c[1])
    expect(labels).toEqual([
      "export_step_init",
      "export_step_render",
      "export_step_compose",
      "export_step_compose",
      "export_step_save",
      "export_step_done",
    ])
  })
})
