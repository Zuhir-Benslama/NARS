// Manual type declarations for packages that ship their own types but are
// blocked by the explicit "types" array in tsconfig.json.

declare module 'html2canvas' {
    interface Options {
        useCORS?:                boolean
        allowTaint?:             boolean
        backgroundColor?:        string | null
        scale?:                  number
        logging?:                boolean
        imageTimeout?:           number
        foreignObjectRendering?: boolean
        width?:                  number
        height?:                 number
    }
    function html2canvas(element: HTMLElement, options?: Options): Promise<HTMLCanvasElement>
    export default html2canvas
}

declare module 'jspdf' {
    interface jsPDFOptions {
        orientation?: 'portrait' | 'landscape' | 'p' | 'l'
        unit?:        'pt' | 'mm' | 'cm' | 'in' | 'px'
        format?:      string | [number, number]
    }
    class jsPDF {
        constructor(options?: jsPDFOptions)
        addImage(data: string, format: string, x: number, y: number, w: number, h: number): this
        setFillColor(r: number, g: number, b: number): this
        setTextColor(r: number, g: number, b: number): this
        setFontSize(size: number): this
        setFont(font: string, style?: string): this
        rect(x: number, y: number, w: number, h: number, style?: string): this
        text(text: string, x: number, y: number, options?: { align?: string }): this
        save(filename: string): this
    }
    export default jsPDF
}
