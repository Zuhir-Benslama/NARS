"""Check docs/uml/nars-vite-component-diagram.md against the nars-web source tree.

The mermaid render gate (make docs-lint-uml) only proves the diagram parses;
it cannot notice a component, store, or member being deleted or renamed in
code. This script closes that gap the way check_uml_class_diagram.py does for
the backend: every ``class`` in the vite component diagram must resolve to a
real nars-web module, and every listed member must resolve to an identifier in
that module's source file(s).

The frontend diagram mixes several kinds of "classes", each resolved by its own
rule in CLASS_FILES:

  - TypeScript types/interfaces (FeatureData, LayerEntry, ...)
  - Pinia stores (AppStore, ModalStore, ...)
  - map/* modules (MapInit, DrawSave, Geometry, ...)
  - composables (useTheme, ...)
  - Vue components (PhaseBar, SettingsModal, ...)
  - wrapper modules (ApiModule, I18n, Config)

A class may map to several files when its member list spans a component and its
bridge modules (e.g. FeatureModal -> FeatureModal.vue + stores/modalStore.ts,
which owns the openCreate/openEdit bridge).

The sequence diagrams under docs/uml describe interactions, not members, so
they stay under render-only enforcement (same as the backend sequence diagram).

Usage:
    python3 nars-infra/scripts/check_uml_vite_component_diagram.py
"""

from __future__ import annotations

import re
import sys
from dataclasses import dataclass, field
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
DIAGRAM = REPO_ROOT / "docs/uml/nars-vite-component-diagram.md"
SRC_DIR = REPO_ROOT / "nars-web" / "src"

# Diagram classes that name a TypeScript type/interface declared in code; the
# literal name must appear in the mapped file(s), not just the file existing.
NAMED_TYPES = {
    "DeletedFeature",
    "FeatureData",
    "LayerEntry",
    "MaplibreFeature",
}

# Diagram class -> source file(s) relative to nars-web/src. .vue entries are the
# component files themselves (matched by stem); .ts/.tsx entries are modules
# whose exported members the diagram documents.
CLASS_FILES: dict[str, list[str]] = {
    # ---- types / interfaces ----
    "FeatureData": ["types/features.ts"],
    "LayerEntry": ["types/features.ts"],
    "MaplibreFeature": ["map/core/state.ts"],
    "DeletedFeature": ["stores/undoStore.ts"],
    # ---- pinia stores ----
    "AppStore": ["stores/appStore.ts"],
    "FeaturesStore": ["stores/featuresStore.ts"],
    "ModalStore": ["stores/modalStore.ts"],
    "LayerStore": ["stores/layerStore.ts"],
    "DrawStore": ["stores/drawStore.ts"],
    "EditStore": ["stores/editStore.ts"],
    "SnapStore": ["stores/snapStore.ts"],
    "UndoStore": ["stores/undoStore.ts"],
    "SelectionStore": ["stores/selectionStore.ts"],
    "RotationStore": ["stores/rotationStore.ts"],
    "FieldStore": ["stores/fieldStore.ts"],
    "ToastStore": ["stores/toastStore.ts"],
    "ConfirmStore": ["stores/confirmStore.ts"],
    "ContextMenuStore": ["stores/contextMenuStore.ts"],
    # ---- api / i18n / config wrapper modules ----
    "ApiModule": ["api/index.ts"],
    "I18n": ["i18n/index.ts"],
    "Config": ["config/index.ts"],
    # ---- map modules ----
    "MapContext": ["map/core/state.ts"],
    "MapInit": ["map/map-init.ts", "map/index.ts"],
    "DrawEvents": ["map/draw/draw-events.ts"],
    "DrawSave": ["map/draw/draw-save.ts"],
    "DrawControl": ["map/draw/draw-control.ts"],
    "DrawHandlers": ["map/draw/draw-handlers.ts"],
    "EditMode": ["map/edit/edit-mode.ts"],
    "Snapping": ["map/snapping/snapping.ts"],
    "SnapSearch": ["map/snapping/snap-search.ts"],
    "SnapGeometry": ["map/snapping/snap-geometry.ts"],
    "FeatureDataModule": ["map/features/feature-data.ts"],
    "FeaturePersistence": ["map/features/feature-persistence.ts"],
    "FeatureLoader": ["map/features/loader.ts", "map/features/loader-db.ts"],
    "PhaseNav": ["map/phases/index.ts"],
    "HouseNumbering": ["map/house-numbering.ts"],
    "Undo": ["map/undo.ts"],
    "RoadDirections": ["map/roads/road-directions.ts"],
    "RoadGraph": ["map/roads/road-graph.ts"],
    "RoadOrient": ["map/roads/road-orient.ts"],
    "Labels": ["map/rendering/labels.ts"],
    "Geometry": ["map/rendering/geometry.ts"],
    "ContextMenu": ["map/context-menu/context-menu.ts"],
    "NamingPanels": ["map/naming-panels.ts"],
    # ---- composables ----
    "UseTheme": ["composables/useTheme.ts"],
    "UseWindowKeydown": ["composables/useWindowKeydown.ts"],
    "UseFeatureValidation": ["composables/useFeatureValidation.ts"],
    "UseFocusTrap": ["composables/useFocusTrap.ts"],
    # ---- components ----
    "App": ["App.vue"],
    "PhaseBar": ["components/PhaseBar.vue"],
    "InfoPanel": ["components/InfoPanel.vue"],
    "ProfileMenu": ["components/ProfileMenu.vue"],
    "TileControl": ["components/TileControl.vue"],
    "FeatureModal": [
        "components/FeatureModal.vue",
        "map/features/feature-modal.ts",
        "stores/modalStore.ts",
    ],
    "SettingsModal": ["components/SettingsModal.vue"],
    "SettingsGeneral": ["components/settings/SettingsGeneral.vue"],
    "SettingsAccount": ["components/settings/SettingsAccount.vue"],
    "SettingsUsers": ["components/settings/SettingsUsers.vue"],
    "SettingsAbout": ["components/settings/SettingsAbout.vue"],
    "AdminDashboard": ["components/AdminDashboard.vue"],
    "AreaTypeSelector": ["components/modals/AreaTypeSelector.vue"],
    "BuildingTypeSelector": ["components/modals/BuildingTypeSelector.vue"],
    "StatPill": ["components/admin/StatPill.vue"],
    "DairaList": ["components/admin/DairaList.vue"],
    "CommuneList": ["components/admin/CommuneList.vue"],
    "ContextMenuCmp": ["components/ContextMenu.vue"],
    "EditSaveButton": ["components/EditSaveButton.vue"],
    "FieldPanel": ["components/FieldPanel.vue"],
    "RoadInspectionForm": ["components/inspection/RoadInspectionForm.vue"],
    "EntranceInspectionForm": ["components/inspection/EntranceInspectionForm.vue"],
    "NamingPanelInspectionForm": [
        "components/inspection/NamingPanelInspectionForm.vue"
    ],
    "ToastContainer": ["components/ToastContainer.vue"],
    "ConfirmDialogCmp": ["components/ConfirmDialog.vue"],
    "WilayaDetailPage": ["components/WilayaDetailPage.vue"],
}

_CLASS_RE = re.compile(r"^\s*class\s+(\w+)")
_TYPE_DECL_RE = re.compile(r"\b(?:class|interface|record|struct|type)\s+([A-Z]\w*)")
_VISIBILITY_RE = re.compile(r"^\s*([+#~-])")


@dataclass
class DiagramClass:
    name: str
    members: list[str] = field(default_factory=list)


def extract_diagram() -> list[DiagramClass]:
    """Parse the classDiagram block into classes and their listed members."""
    text = DIAGRAM.read_text(encoding="utf-8")
    block_start = text.find("```mermaid")
    if block_start == -1:
        sys.exit(f"✖ No ```mermaid block found in {DIAGRAM.name}")
    block_start += len("```mermaid\n")
    block_end = text.find("```", block_start)
    if block_end == -1:
        sys.exit(f"✖ Unclosed ```mermaid block in {DIAGRAM.name}")
    block = text[block_start:block_end]

    classes: list[DiagramClass] = []
    current: DiagramClass | None = None
    for line in block.splitlines():
        class_match = _CLASS_RE.match(line)
        if class_match:
            current = DiagramClass(name=class_match.group(1))
            classes.append(current)
            continue
        if current is None or not line.strip():
            continue
        member_match = _VISIBILITY_RE.match(line)
        if not member_match:
            continue
        rest = line[member_match.end() :].strip()
        if "(" in rest:
            name = rest[: rest.index("(")].strip()
        else:
            name = rest.split()[-1]
        if name.endswith("..."):
            name = name[:-3].strip()
        if name.endswith("?"):
            name = name[:-1].strip()
        if name:
            current.members.append(name)
    return classes


def resolve_files(class_name: str) -> list[Path]:
    """Map a diagram class to its source file(s), or [] if unknown."""
    if class_name not in CLASS_FILES:
        return []
    return [SRC_DIR / rel for rel in CLASS_FILES[class_name]]


def type_declared(files: list[Path], name: str) -> bool:
    """True if ``name`` is declared as a class/interface/record/struct/type in one of ``files``."""
    for file in files:
        if not file.is_file():
            continue
        text = file.read_text(encoding="utf-8", errors="replace")
        if _TYPE_DECL_RE.search(text) and re.search(rf"\b{re.escape(name)}\b", text):
            return True
    return False


def member_visible(files: list[Path], name: str) -> bool:
    """True if ``name`` appears as an identifier in any of ``files``."""
    pattern = re.compile(rf"\b{re.escape(name)}\b")
    for file in files:
        if not file.is_file():
            continue
        text = file.read_text(encoding="utf-8", errors="replace")
        if pattern.search(text):
            return True
    return False


def main() -> None:
    if not DIAGRAM.is_file():
        sys.exit(f"✖ Diagram not found: {DIAGRAM}")
    if not SRC_DIR.is_dir():
        sys.exit(f"✖ nars-web source dir not found: {SRC_DIR}")

    errors: list[str] = []
    for class_entry in extract_diagram():
        name = class_entry.name
        files = resolve_files(name)
        if not files:
            errors.append(f"  class '{name}' has no source mapping")
            continue
        missing_files = [f for f in files if not f.is_file()]
        if missing_files:
            errors.append(
                f"  class '{name}' resolves to missing file(s):"
                f" {', '.join(str(f.relative_to(REPO_ROOT)) for f in missing_files)}"
            )
            continue
        if name in NAMED_TYPES and not type_declared(files, name):
            errors.append(f"  type '{name}' is not declared in its source file(s)")
            continue
        for member in class_entry.members:
            if not member_visible(files, member):
                errors.append(
                    f"  member '{name}.{member}' not found in its source file(s)"
                )

    if errors:
        print(f"✖ {DIAGRAM.name} drifted from nars-web source:")
        print("\n".join(errors))
        print("  Regenerate the diagram or fix the listed entries.")
        sys.exit(1)
    print(f"✓ {DIAGRAM.name} matches nars-web source")


if __name__ == "__main__":
    main()
