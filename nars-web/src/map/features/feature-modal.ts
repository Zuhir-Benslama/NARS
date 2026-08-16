import { PHASES } from "../../phases"
import { useAppStore } from "../../stores/appStore"
import { useModalStore } from "../../stores/modalStore"
import { checkMainUrbanExists } from "../../lib/validation"
export async function prepareModalExtras(phase: (typeof PHASES)[number]): Promise<void> {
  const modalStore = useModalStore()
  const appStore = useAppStore()

  if (phase.key === "areas") {
    const mainUrbanExists = await checkMainUrbanExists()
    modalStore.patchFields({
      mainUrbanExists,
      areaTypeKey: mainUrbanExists ? "secondary_urban" : "central_urban",
      label: mainUrbanExists ? "" : appStore.communeName,
    })
  }
}
