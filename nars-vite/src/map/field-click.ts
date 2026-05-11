import { ctx } from "./core/state"
import { useFieldStore } from "../stores/fieldStore"
import { useAppStore } from "../stores/appStore"
import type { InspectionType } from "../types/inspection"
import type { MapMouseEvent } from "maplibre-gl"

export function registerFieldWorkerClick(): void {
  const map = ctx.map
  const appStore = useAppStore()

  if (appStore.user?.role !== "field_worker") return

  map.on("click", (e: MapMouseEvent) => {
    const features = map.queryRenderedFeatures(e.point, {
      layers: ["nars-point", "nars-line", "nars-polygon-fill", "nars-polygon-stroke"],
    })

    if (features.length === 0) return

    const feature = features[0]
    const props = feature.properties
    if (!props) return

    const fieldStore = useFieldStore()
    const type = mapPhaseKeyToInspectionType(props.phaseKey as string)
    if (!type) return

    fieldStore.selectFeature({
      id: props.dbId as string,
      label: (props.label as string) || `Unnamed ${type}`,
      type,
    })
  })
}

function mapPhaseKeyToInspectionType(phaseKey: string): InspectionType | null {
  switch (phaseKey) {
    case "roads":
      return "road"
    case "houseEntrances":
      return "house_entrance"
    case "namingPanels":
      return "naming_panel"
    default:
      return null
  }
}
