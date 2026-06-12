import { t, currentLang } from "../i18n"

export type PaperSize = "A0" | "A3"

type Html2Canvas = (
  el: HTMLElement,
  opts?: Record<string, unknown>,
) => Promise<HTMLCanvasElement>

interface JsPDFInstance {
  save: (name: string) => void
  addImage: (...args: unknown[]) => void
  setFillColor: (...args: unknown[]) => void
  rect: (...args: unknown[]) => void
  setTextColor: (...args: unknown[]) => void
  setFontSize: (...args: unknown[]) => void
  setFont: (...args: unknown[]) => void
  text: (...args: unknown[]) => void
}

type JsPDFConstructor = new (...args: unknown[]) => JsPDFInstance

interface PdfDeps {
  html2canvas: Html2Canvas
  jsPDF: JsPDFConstructor
}

async function loadPdfDeps(): Promise<PdfDeps> {
  try {
    const mods = await Promise.all([
      import(/* @vite-ignore */ "html2canvas" as string) as Promise<{
        default: Html2Canvas
      }>,
      import(/* @vite-ignore */ "jspdf" as string) as Promise<{ default: JsPDFConstructor }>,
    ])
    const html2canvas = mods[0].default
    const jsPDF = mods[1].default
    if (!html2canvas || !jsPDF) {
      throw new Error("Missing exports")
    }
    return { html2canvas, jsPDF }
  } catch {
    throw new Error(
      "PDF export requires html2canvas and jspdf. " +
        "Install them with: npm install html2canvas jspdf",
    )
  }
}

const PAPER: Record<PaperSize, [number, number]> = {
  A3: [420, 297],
  A0: [1189, 841],
}

function renderTitleBar(
  pdf: JsPDFInstance,
  pageW: number,
  pageH: number,
  margin: number,
  size: PaperSize,
  municipalityName: string,
): void {
  const barY = pageH - margin - 10
  pdf.setFillColor(26, 35, 60)
  pdf.rect(margin, barY, pageW - margin * 2, 10, "F")
  pdf.setTextColor(255, 255, 255)
  pdf.setFontSize(size === "A0" ? 11 : 8)
  pdf.setFont("helvetica", "bold")
  pdf.text(`NARS — ${municipalityName}`, margin + 3, barY + 6.5)
  pdf.setFont("helvetica", "normal")
  pdf.setFontSize(size === "A0" ? 9 : 7)
  const dateStr = new Date().toLocaleDateString(currentLang.value, {
    year: "numeric",
    month: "long",
    day: "numeric",
  })
  pdf.text(`${t("export_paper_size")}: ${size}   |   ${dateStr}`, pageW - margin - 3, barY + 6.5, {
    align: "right",
  })
}

function computeImageDimensions(
  canvasW: number,
  canvasH: number,
  printW: number,
  printH: number,
): { imgW: number; imgH: number } {
  const aspect = canvasW / canvasH
  const pAspect = printW / printH
  if (aspect > pAspect) {
    return { imgW: printW, imgH: printW / aspect }
  }
  return { imgH: printH, imgW: printH * aspect }
}

export async function exportMapToPdf(
  size: PaperSize,
  municipalityName: string,
  onProgress: (pct: number, label: string) => void = () => undefined,
): Promise<void> {
  const mapEl = document.getElementById("map")
  if (!mapEl) throw new Error("Map element not found")

  onProgress(5, t("export_step_init"))

  const { html2canvas, jsPDF } = await loadPdfDeps()

  onProgress(15, t("export_step_render"))

  let canvas: HTMLCanvasElement | null = null
  try {
    canvas = await html2canvas(mapEl, {
      useCORS: true,
      allowTaint: false,
      backgroundColor: "#000",
      scale: size === "A0" ? 3 : 2,
      logging: false,
      imageTimeout: 15000,
      foreignObjectRendering: false,
    })
    onProgress(82, t("export_step_compose"))
  } catch (err) {
    canvas = null
    throw err
  }

  const [pageW, pageH] = PAPER[size]
  const margin = 10

  const pdf = new jsPDF({
    orientation: "landscape",
    unit: "mm",
    format: [pageW, pageH],
  })

  const printW = pageW - margin * 2
  const printH = pageH - margin * 2 - 14
  const { imgW, imgH } = computeImageDimensions(canvas.width, canvas.height, printW, printH)

  pdf.addImage(
    canvas.toDataURL("image/jpeg", 0.92),
    "JPEG",
    margin + (printW - imgW) / 2,
    margin,
    imgW,
    imgH,
  )

  onProgress(93, t("export_step_compose"))
  renderTitleBar(pdf, pageW, pageH, margin, size, municipalityName)

  onProgress(97, t("export_step_save"))
  pdf.save(`NARS_${municipalityName.replace(/\s+/g, "_")}_${size}_${Date.now()}.pdf`)
  onProgress(100, t("export_step_done"))
}
