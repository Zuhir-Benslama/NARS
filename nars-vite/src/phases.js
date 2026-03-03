// ─── PHASE DEFINITIONS ────────────────────────────────────────────────────────

export const PHASES = [
    { index: 0, key: 'areas',               label: 'Areas',               drawType: 'polygon',
      color: '#8e44ad',
      hint: 'Draw urban areas (Main Urban or Secondary Urban). Scattered areas are computed automatically.' },
    { index: 1, key: 'cityCenter',          label: 'City Center',         drawType: 'marker',
      color: '#e74c3c',
      hint: 'Place the city center marker. It determines the numbering direction for house entrances.' },
    { index: 2, key: 'districts',           label: 'Districts',           drawType: 'polygon',
      color: '#f39c12',
      hint: 'Draw districts inside urban areas. They must share edges — no gaps allowed.' },
    { index: 3, key: 'roads',               label: 'Roads',               drawType: 'polyline',
      color: '#3498db',
      hint: 'Draw roads inside the municipal limit. Each road must connect to at least one other road. No turn may exceed 90°.' },
    { index: 4, key: 'mainEntrances',       label: 'Main Entrances',      drawType: 'marker',
      color: '#27ae60',
      hint: 'Place main entrances along roads. Left side = odd numbers, right side = even numbers. Numbering restarts per road.' },
    { index: 5, key: 'secondaryEntrances',  label: 'Secondary Entrances', drawType: 'marker',
      color: '#16a085',
      hint: 'Place secondary entrances linked to a main entrance. Numbered BIS01, BIS02… per main entrance.' },
    { index: 6, key: 'publicBuildings',     label: 'Public Buildings',    drawType: 'polygon',
      color: '#e67e22',
      hint: 'Mark public buildings. Allowed everywhere, including scattered areas.' },
    { index: 7, key: 'publicSpaces',        label: 'Public Spaces',       drawType: 'polygon',
      color: '#2ecc71',
      hint: 'Mark public spaces (gardens, squares) inside the municipal limit.' },
];

// API layer value → phase key (for loading saved features from the database)
export const API_LAYER_TO_PHASE = {
    central_urban:      'areas',
    secondary_urban:    'areas',
    // scattered is rendered separately, not added to allFeatures
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
    main_entrance:      'mainEntrances',
    secondary_entrance: 'secondaryEntrances',
    public_building:    'publicBuildings',
    garden:             'publicSpaces',
    square:             'publicSpaces',
};

// ─── FEATURE SUB-TYPES ────────────────────────────────────────────────────────

export const AREA_TYPES = [
    { key: 'central_urban',   label: 'Main Urban Area',      color: '#c0392b' },
    { key: 'secondary_urban', label: 'Secondary Urban Area', color: '#8e44ad' },
];

export const DISTRICT_TYPES = [
    { key: 'housing_estate', label: 'Housing Estate' },
    { key: 'urban_pole',     label: 'Urban Pole'     },
    { key: 'district',       label: 'District'       },
];

export const ROAD_TYPES = [
    { key: 'boulevard',  label: 'Boulevard',  category: 'primary'   },
    { key: 'avenue',     label: 'Avenue',     category: 'primary'   },
    { key: 'street',     label: 'Street',     category: 'secondary' },
    { key: 'drive',      label: 'Drive',      category: 'tertiary'  },
    { key: 'lane',       label: 'Lane',       category: 'tertiary'  },
    { key: 'cul_de_sac', label: 'Cul-de-sac', category: 'tertiary'  },
    { key: 'way',        label: 'Way',        category: 'tertiary'  },
];

export const PUBLIC_SPACE_TYPES = [
    { key: 'garden', label: 'Garden', color: '#27ae60' },
    { key: 'square', label: 'Square', color: '#2980b9' },
];
