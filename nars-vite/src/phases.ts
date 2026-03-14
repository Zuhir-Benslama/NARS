import type { Phase, AreaType, DistrictType, RoadType, PublicSpaceType } from './types'

// ─── PHASE DEFINITIONS ────────────────────────────────────────────────────────

export const PHASES: Phase[] = [
    { index: 0, key: 'areas',              label: 'Areas',               drawType: 'polygon',
      color: '#8e44ad',
      hint: 'Draw urban areas (Main Urban or Secondary Urban). Scattered areas are computed automatically.' },
    { index: 1, key: 'districts',          label: 'Districts',           drawType: 'polygon',
      color: '#f39c12',
      hint: 'Draw districts inside urban areas. They must share edges — no gaps allowed.' },
    { index: 2, key: 'cityCenter',         label: 'City Center',         drawType: 'circle',
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
    { index: 7, key: 'namingPanels',       label: 'Naming Panels',       drawType: 'marker',
      color: '#9b59b6',
      hint: 'Place naming panels (signage) at appropriate locations.' },
]

// API layer value → phase key (for loading saved features from the database)
export const API_LAYER_TO_PHASE: Record<string, string> = {
    central_urban:      'areas',
    secondary_urban:    'areas',
    city_center:        'cityCenter',
    housing_estate:         'districts',
    urban_pole:             'districts',
    district:               'districts',
    trad_activities_zone:   'districts',
    industry_zone:          'districts',
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
    // building sub-type keys
    bank:                            'publicBuildings',
    post_office:                     'publicBuildings',
    convention_centre:               'publicBuildings',
    public_market:                   'publicBuildings',
    trade_centre:                    'publicBuildings',
    library:                         'publicBuildings',
    museum:                          'publicBuildings',
    theater:                         'publicBuildings',
    borders_guard:                   'publicBuildings',
    customs:                         'publicBuildings',
    fire_station:                    'publicBuildings',
    gendarmes:                       'publicBuildings',
    military_barrack:                'publicBuildings',
    police_station:                  'publicBuildings',
    administrative_branch:           'publicBuildings',
    public_hospital:                 'publicBuildings',
    neighborhood_health:             'publicBuildings',
    specialized_hospital:            'publicBuildings',
    treatment_room:                  'publicBuildings',
    university_hospital:             'publicBuildings',
    research_institute:              'publicBuildings',
    university:                      'publicBuildings',
    college:                         'publicBuildings',
    school:                          'publicBuildings',
    cemetery:                        'publicBuildings',
    mosque:                          'publicBuildings',
    hostel:                          'publicBuildings',
    hotel:                           'publicBuildings',
    motel:                           'publicBuildings',
    airport:                         'publicBuildings',
    bus_station:                     'publicBuildings',
    train_station:                   'publicBuildings',
    specialized_vocational_institute:'publicBuildings',
    vocational_education_institute:  'publicBuildings',
    vocational_apprenticeship_center:'publicBuildings',
    vocational_training_institute:   'publicBuildings',
    indoor_arena:                    'publicBuildings',
    leisure_center:                  'publicBuildings',
    sports_complex:                  'publicBuildings',
    stadium:                         'publicBuildings',
    swimming_pool:                   'publicBuildings',
    youth_clubs:                     'publicBuildings',
    youth_hostel:                    'publicBuildings',
    garden:             'publicSpaces',
    square:             'publicSpaces',
}

// ─── FEATURE SUB-TYPES ────────────────────────────────────────────────────────

export const AREA_TYPES: AreaType[] = [
    { key: 'central_urban',   label: 'Main Urban Area',      color: '#c0392b' },
    { key: 'secondary_urban', label: 'Secondary Urban Area', color: '#8e44ad' },
]

export const DISTRICT_TYPES: DistrictType[] = [
    { key: 'housing_estate',        label: 'Housing Estate'        },
    { key: 'urban_pole',            label: 'Urban Pole'            },
    { key: 'district',              label: 'District'              },
    { key: 'trad_activities_zone',  label: 'Trad. Activities Zone' },
    { key: 'industry_zone',         label: 'Industry Zone', allowInScattered: true },
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

// ─── PUBLIC BUILDING SECTORS & TYPES ─────────────────────────────────────────

export interface PublicBuildingType {
    key:   string
    label: string
}

export interface PublicBuildingSector {
    key:       string
    label:     string
    buildings: PublicBuildingType[]
}

export const PUBLIC_BUILDING_SECTORS: PublicBuildingSector[] = [
    { key: 'banking_postal', label: 'Banking & Postal', buildings: [
        { key: 'bank',        label: 'Bank'         },
        { key: 'post_office', label: 'Post Offices'  },
    ]},
    { key: 'commerce', label: 'Commerce', buildings: [
        { key: 'convention_centre', label: 'Convention Centres' },
        { key: 'public_market',     label: 'Public Markets'     },
        { key: 'trade_centre',      label: 'Trade Centres'      },
    ]},
    { key: 'culture', label: 'Culture', buildings: [
        { key: 'library', label: 'Libraries' },
        { key: 'museum',  label: 'Museum'    },
        { key: 'theater', label: 'Theaters'  },
    ]},
    { key: 'defence_security', label: 'Defence and Security', buildings: [
        { key: 'borders_guard',    label: 'Borders Guard Unit' },
        { key: 'customs',          label: 'Customs Unit'       },
        { key: 'fire_station',     label: 'Fire Station Unit'  },
        { key: 'gendarmes',        label: 'Gendarmes Unit'     },
        { key: 'military_barrack', label: 'Military Barrack'   },
        { key: 'police_station',   label: 'Police Station'     },
    ]},
    { key: 'government_law', label: 'Government & Law', buildings: [
        { key: 'administrative_branch', label: 'Administrative Branch' },
    ]},
    { key: 'healthcare', label: 'Healthcare', buildings: [
        { key: 'public_hospital',     label: 'Public Hospital Establishment'              },
        { key: 'neighborhood_health', label: 'Public Neighborhood Health Establishment'   },
        { key: 'specialized_hospital',label: 'Specialized Hospital Establishment'         },
        { key: 'treatment_room',      label: 'Treatment Room'                             },
        { key: 'university_hospital', label: 'University Hospital Center'                 },
    ]},
    { key: 'higher_education', label: 'Higher Education', buildings: [
        { key: 'research_institute', label: 'Research Institute' },
        { key: 'university',         label: 'University'         },
    ]},
    { key: 'national_education', label: 'National Education', buildings: [
        { key: 'college', label: 'College'   },
        { key: 'library', label: 'Libraries' },
        { key: 'school',  label: 'School'    },
    ]},
    { key: 'religious', label: 'Religious', buildings: [
        { key: 'cemetery', label: 'Cemetery' },
        { key: 'mosque',   label: 'Mosque'   },
    ]},
    { key: 'tourism', label: 'Tourism', buildings: [
        { key: 'hostel', label: 'Hostel' },
        { key: 'hotel',  label: 'Hotel'  },
        { key: 'motel',  label: 'Motel'  },
    ]},
    { key: 'transport', label: 'Transport', buildings: [
        { key: 'airport',       label: 'Airport'       },
        { key: 'bus_station',   label: 'Bus Station'   },
        { key: 'train_station', label: 'Train Station' },
    ]},
    { key: 'vocational_training', label: 'Vocational Training and Education', buildings: [
        { key: 'specialized_vocational_institute',   label: 'National Specialized Vocational Training Institute' },
        { key: 'vocational_education_institute',     label: 'Vocational Education Institute'                    },
        { key: 'vocational_apprenticeship_center',   label: 'Vocational Training and Apprenticeship Center'     },
        { key: 'vocational_training_institute',      label: 'Vocational Training Institute'                     },
    ]},
    { key: 'youth_sports', label: 'Youth & Sports', buildings: [
        { key: 'indoor_arena',   label: 'Indoor Arena'   },
        { key: 'leisure_center', label: 'Leisure Center' },
        { key: 'sports_complex', label: 'Sports Complex' },
        { key: 'stadium',        label: 'Stadium'        },
        { key: 'swimming_pool',  label: 'Swimming Pool'  },
        { key: 'youth_clubs',    label: 'Youth Clubs'    },
        { key: 'youth_hostel',   label: 'Youth Hostel'   },
    ]},
]
