// ─── MAP EXPORT ───────────────────────────────────────────────────────────────
// NOTE: html2canvas and jspdf are optional dependencies. The export feature
// is only available when they are installed. Run:
//   npm install html2canvas jspdf
// to enable PDF export functionality.

import { t, currentLang } from "../i18n"

export type PaperSize = "A0" | "A3"

export async function exportMapToPdf(
  size: PaperSize,
  municipalityName: string,
  onProgress: (pct: number, label: string) => void = () => undefined,
): Promise<void> {
  const mapEl = document.getElementById("map")
  if (!mapEl) throw new Error("Map element not found")

  onProgress(5, t("export_step_init"))

  // Dynamic imports — these will fail if the packages are not installed,
  // providing a clear error message instead of a cryptic "module not found".
  // Using string literal imports bypasses TS type resolution for optional deps.
  /* eslint-disable @typescript-eslint/no-explicit-any */
  let html2canvas: any
  let jsPDF: any
  /* eslint-enable @typescript-eslint/no-explicit-any */
  try {
    const mods = await Promise.all([
      import(/* @vite-ignore */ "html2canvas" as string),
      import(/* @vite-ignore */ "jspdf" as string),
    ])
    html2canvas = mods[0]?.default
    jsPDF = mods[1]?.default
    if (!html2canvas || !jsPDF) {
      throw new Error("Missing exports")
    }
  } catch {
    throw new Error(
      "PDF export requires html2canvas and jspdf. " +
        "Install them with: npm install html2canvas jspdf",
    )
  }

  onProgress(15, t("export_step_render"))

  let canvas: HTMLCanvasElement
  try {
    canvas = await html2canvas(mapEl, {
      useCORS: true,
      allowTaint: false,
      backgroundColor: "#000",
      scale: size === "A0" ? 3 : 2,
      logging: false,
      imageTimeout: 15000,
      foreignObjectRendering: false, // Security: prevent SVG foreignObject XSS
    })
    onProgress(82, t("export_step_compose"))
  } catch (err) {
    // Clean up canvas reference on failure
    canvas = undefined as unknown as HTMLCanvasElement
    throw err
  }

  const PAPER: Record<PaperSize, [number, number]> = {
    A3: [420, 297],
    A0: [1189, 841],
  }

  const [pageW, pageH] = PAPER[size]
  const margin = 10

  // jsPDF's Options type is a union; the runtime accepts { orientation, unit, format }.
  // We cast to the first union member to satisfy the type checker.
  const pdf = new jsPDF({
    orientation: "landscape",
    unit: "mm",
    format: [pageW, pageH],
  })

  const printW = pageW - margin * 2
  const printH = pageH - margin * 2 - 14
  const aspect = canvas.width / canvas.height
  const pAspect = printW / printH
  let imgW: number, imgH: number
  if (aspect > pAspect) {
    imgW = printW
    imgH = printW / aspect
  } else {
    imgH = printH
    imgW = printH * aspect
  }

  pdf.addImage(
    canvas.toDataURL("image/jpeg", 0.92),
    "JPEG",
    margin + (printW - imgW) / 2,
    margin,
    imgW,
    imgH,
  )

  onProgress(93, t("export_step_compose"))

  // Title bar
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

  onProgress(97, t("export_step_save"))
  pdf.save(`NARS_${municipalityName.replace(/\s+/g, "_")}_${size}_${Date.now()}.pdf`)
  onProgress(100, t("export_step_done"))
}
