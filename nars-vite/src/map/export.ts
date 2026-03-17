// ─── MAP EXPORT ───────────────────────────────────────────────────────────────
// Exports the current map view as a PDF in A0 or A3 landscape format.
//
// Satellite tiles are served from /api/satellite/tile (same-origin MPC proxy)
// so the canvas is never tainted — all layers export cleanly with html2canvas.

import { ctx } from './state'
import { t }   from '../i18n'

export type PaperSize = 'A0' | 'A3'

const PAPER: Record<PaperSize, [number, number]> = {
    A3: [420, 297],
    A0: [1189, 841],
}
const SCALE: Record<PaperSize, number> = { A3: 2, A0: 3 }

export async function exportMapToPdf(
    size:             PaperSize,
    municipalityName: string,
    onProgress:       (pct: number, label: string) => void = () => {}
): Promise<void> {
    const mapEl = document.getElementById('map')
    if (!mapEl) throw new Error('Map element not found')

    // ── 1. Load libraries ─────────────────────────────────────────────────────
    onProgress(5, t('export_step_init'))
    const [{ default: html2canvas }, { default: jsPDF }] = await Promise.all([
        import('html2canvas'),
        import('jspdf'),
    ])

    // ── 2. No satellite swap needed ───────────────────────────────────────────
    // Satellite tiles are now served from /api/satellite/tile (same-origin)
    // so the canvas is never tainted — no layer swap required.

    // ── 3. Detach UI chrome so it doesn't appear in the export ────────────────
    onProgress(15, t('export_step_render'))
    const detached: { el: HTMLElement; parent: HTMLElement; next: ChildNode | null }[] = []
    mapEl.querySelectorAll<HTMLElement>('.leaflet-control-container').forEach(el => {
        const parent = el.parentElement
        if (!parent) return
        detached.push({ el, parent, next: el.nextSibling })
        parent.removeChild(el)
    })

    // ── 4. Capture ────────────────────────────────────────────────────────────
    let pct = 15
    const timer = setInterval(() => {
        pct = Math.min(80, pct + (size === 'A0' ? 0.3 : 0.7))
        onProgress(Math.round(pct), t('export_step_render'))
    }, 200)

    let canvas: HTMLCanvasElement
    try {
        canvas = await html2canvas(mapEl, {
            useCORS:                true,
            allowTaint:             false,
            backgroundColor:        '#000',
            scale:                  SCALE[size],
            logging:                false,
            imageTimeout:           15000,
            foreignObjectRendering: true,
        })
    } finally {
        clearInterval(timer)
        // Always restore UI chrome
        detached.forEach(({ el, parent, next }) => parent.insertBefore(el, next))
        // Live map untouched — nothing to restore
    }
    onProgress(82, t('export_step_compose'))

    // ── 5. Compose PDF ────────────────────────────────────────────────────────
    const [pageW, pageH] = PAPER[size]
    const margin = 10

    // @ts-ignore
    const pdf = new jsPDF({ orientation: 'landscape', unit: 'mm', format: [pageW, pageH] })

    const printW  = pageW - margin * 2
    const printH  = pageH - margin * 2 - 14
    const aspect  = canvas.width / canvas.height
    const pAspect = printW / printH
    let imgW: number, imgH: number
    if (aspect > pAspect) { imgW = printW; imgH = printW / aspect }
    else                   { imgH = printH; imgW = printH * aspect }

    pdf.addImage(
        canvas.toDataURL('image/jpeg', 0.92), 'JPEG',
        margin + (printW - imgW) / 2, margin, imgW, imgH
    )
    onProgress(93, t('export_step_compose'))

    // Title bar
    const barY = pageH - margin - 10
    pdf.setFillColor(26, 35, 60)
    pdf.rect(margin, barY, pageW - margin * 2, 10, 'F')
    pdf.setTextColor(255, 255, 255)
    pdf.setFontSize(size === 'A0' ? 11 : 8)
    pdf.setFont('helvetica', 'bold')
    pdf.text(`NARS — ${municipalityName}`, margin + 3, barY + 6.5)
    pdf.setFont('helvetica', 'normal')
    pdf.setFontSize(size === 'A0' ? 9 : 7)
    const dateStr = new Date().toLocaleDateString('fr-DZ', {
        year: 'numeric', month: 'long', day: 'numeric',
    })
    pdf.text(
        `${t('export_paper_size')}: ${size}   |   ${dateStr}`,
        pageW - margin - 3, barY + 6.5, { align: 'right' }
    )

    onProgress(97, t('export_step_save'))
    pdf.save(`NARS_${municipalityName.replace(/\s+/g, '_')}_${size}_${Date.now()}.pdf`)
    onProgress(100, t('export_step_done'))
}
