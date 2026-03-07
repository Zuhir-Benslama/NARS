import type { Phase, AreaType, DistrictType, RoadType, PublicSpaceType } from './types'

// ─── PHASE DEFINITIONS ────────────────────────────────────────────────────────

export const PHASES: Phase[] = [
    { index: 0, key: 'areas',              label: 'Areas',               drawType: 'polygon',
      color: '#8e44ad',
      hint: 'Draw urban areas (Main Urban or Secondary Urban). Scattered areas are computed automatically.' },
    { index: 1, key: 'districts',          label: 'Districts',           drawType: 'polygon',
      color: '#f39c12',
      hint: 'Draw districts inside urban areas. They must share edges — no gaps allowed.' },
    { index: 2, key: 'cityCenter',         label: 'City Center',         drawType: 'marker',
      color: '#e74c3c',
      hint: 'Place the city center marker. It determines the numbering direction for house entrances.' },
    { index: 3, key: 'roads',              label: 'Roads',               drawType: 'polyline',
      color: '#3498db',
      hint: 'Draw roads inside the municipal limit. Each road must connect to at least one other road. No turn may exceed 90°.' },
    { index: 4, key: 'houseEntrances',     label: 'House Entrances',     drawType: 'marker',
      color: '#27ae60',
      hint: 'Place main entrances along roads (left = odd, right = even), then secondary entrances linked to a main entrance (BIS01, BIS02…).' },
    { index: 5, key: 'publicBuildings',    label: 'Public Buildings',    drawType: 'polygon',
      color: '#e67e22',
      hint: 'Mark public buildings. Allowed everywhere, including scattered areas.' },
    { index: 6, key: 'publicSpaces',       label: 'Public Spaces',       drawType: 'polygon',
      color: '#2ecc71',
      hint: 'Mark public spaces (gardens, squares) inside the municipal limit.' },
]

// API layer value → phase key (for loading saved features from the database)
export const API_LAYER_TO_PHASE: Record<string, string> = {
    central_urban:      'areas',
    secondary_urban:    'areas',
    city_center:        'cityCenter',
    housing_estate:     'districts',
    urban_pole:         'districts',
    district:           'districts',
    boulevard:          'roads',
    avenue:             'roads',
    street:             'roads',
    drive:              'roads',
    lane:               'roads',
    cul_de_sac:         'roads',
    way:                'roads',
    main_entrance:      'houseEntrances',
    secondary_entrance: 'houseEntrances',
    public_building:    'publicBuildings',
    garden:             'publicSpaces',
    square:             'publicSpaces',
}

// ─── FEATURE SUB-TYPES ────────────────────────────────────────────────────────

export const AREA_TYPES: AreaType[] = [
    { key: 'central_urban',   label: 'Main Urban Area',      color: '#c0392b' },
    { key: 'secondary_urban', label: 'Secondary Urban Area', color: '#8e44ad' },
]

export const DISTRICT_TYPES: DistrictType[] = [
    { key: 'housing_estate', label: 'Housing Estate' },
    { key: 'urban_pole',     label: 'Urban Pole'     },
    { key: 'district',       label: 'District'       },
]

export const ROAD_TYPES: RoadType[] = [
    { key: 'boulevard',  label: 'Boulevard',  category: 'primary'   },
    { key: 'avenue',     label: 'Avenue',     category: 'primary'   },
    { key: 'street',     label: 'Street',     category: 'secondary' },
    { key: 'drive',      label: 'Drive',      category: 'tertiary'  },
    { key: 'lane',       label: 'Lane',       category: 'tertiary'  },
    { key: 'cul_de_sac', label: 'Cul-de-sac', category: 'tertiary'  },
    { key: 'way',        label: 'Way',        category: 'tertiary'  },
]

export const PUBLIC_SPACE_TYPES: PublicSpaceType[] = [
    { key: 'garden', label: 'Garden', color: '#27ae60' },
    { key: 'square', label: 'Square', color: '#2980b9' },
]
