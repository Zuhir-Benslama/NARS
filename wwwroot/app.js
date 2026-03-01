// ═══════════════════════════════════════════════════════════════════════════════
// NARS — National Addressing Reference System
// Phase workflow:
//   1. Areas (urban only — scattered is auto-computed)
//   2. City Center (optional, determines numbering direction)
//   3. Districts (must tile urban areas with no gaps)
//   4. Roads (must connect to existing roads, no turn > 90°)
//   5. Main Entrances (odd = left side, even = right side of road)
//   6. Secondary Entrances (BIS01, BIS02… per main entrance)
//   7. Public Buildings (allowed everywhere incl. scattered areas)
//   8. Public Spaces
// ═══════════════════════════════════════════════════════════════════════════════

// ─── MAP INITIALIZATION ───────────────────────────────────────────────────────

const map = L.map('map').setView([36.7538, 3.0588], 10);
const API_BASE = window.location.protocol === 'file:' ? 'http://localhost:5000' : '';
const apiUrl  = path => `${API_BASE}${path}`;
const apiFetch = (path, options = {}) =>
    fetch(apiUrl(path), { ...options, credentials: options.credentials ?? (API_BASE ? 'omit' : 'same-origin') });

// Tile layers
const satelliteLayer = L.tileLayer('https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}', { attribution: 'Tiles © Esri' });
const streetLayer    = L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png',  { attribution: '© OpenStreetMap contributors' });
const cartoLayer     = L.tileLayer('https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png', { attribution: '© OpenStreetMap © CARTO', subdomains: 'abcd' });
const darkLayer      = L.tileLayer('https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png',  { attribution: '© OpenStreetMap © CARTO', subdomains: 'abcd' });
satelliteLayer.addTo(map);
L.control.layers({ Satellite: satelliteLayer, Street: streetLayer, Light: cartoLayer, Dark: darkLayer }, null, { position: 'bottomleft' }).addTo(map);

// ─── PHASE DEFINITIONS ────────────────────────────────────────────────────────

const PHASES = [
    { index: 0, key: 'areas',               label: 'Areas',               drawType: 'polygon',  color: '#8e44ad',
      hint: 'Draw urban areas (Main Urban or Secondary Urban). Scattered areas are computed automatically.' },
    { index: 1, key: 'cityCenter',          label: 'City Center',         drawType: 'marker',   color: '#e74c3c',
      hint: 'Place the city center marker. It determines the numbering direction for house entrances.' },
    { index: 2, key: 'districts',           label: 'Districts',           drawType: 'polygon',  color: '#f39c12',
      hint: 'Draw districts inside urban areas. They must share edges — no gaps allowed.' },
    { index: 3, key: 'roads',               label: 'Roads',               drawType: 'polyline', color: '#3498db',
      hint: 'Draw roads inside the municipal limit. Each road must connect to at least one other road. No turn may exceed 90°.' },
    { index: 4, key: 'mainEntrances',       label: 'Main Entrances',      drawType: 'marker',   color: '#27ae60',
      hint: 'Place main entrances along roads. Left side = odd numbers, right side = even numbers. Numbering restarts per road.' },
    { index: 5, key: 'secondaryEntrances',  label: 'Secondary Entrances', drawType: 'marker',   color: '#16a085',
      hint: 'Place secondary entrances linked to a main entrance. Numbered BIS01, BIS02… independently per main entrance.' },
    { index: 6, key: 'publicBuildings',     label: 'Public Buildings',    drawType: 'polygon',  color: '#e67e22',
      hint: 'Mark public buildings. Allowed everywhere, including scattered areas.' },
    { index: 7, key: 'publicSpaces',        label: 'Public Spaces',       drawType: 'polygon',  color: '#2ecc71',
      hint: 'Mark public spaces (gardens, squares) inside the municipal limit.' },
];

// API type → phase key mapping (for loading saved features)
const API_LAYER_TO_PHASE = {
    central_urban:       'areas',
    secondary_urban:     'areas',
    // scattered is rendered separately
    city_center:         'cityCenter',
    housing_estate:      'districts',
    urban_pole:          'districts',
    district:            'districts',
    boulevard:           'roads',
    avenue:              'roads',
    street:              'roads',
    drive:               'roads',
    lane:                'roads',
    cul_de_sac:          'roads',
    way:                 'roads',
    main_entrance:       'mainEntrances',
    secondary_entrance:  'secondaryEntrances',
    public_building:     'publicBuildings',
    garden:              'publicSpaces',
    square:              'publicSpaces',
};

// ─── AREA TYPES ───────────────────────────────────────────────────────────────

const AREA_TYPES = [
    { key: 'central_urban',   label: 'Main Urban Area',             color: '#c0392b', dash: null    },
    { key: 'secondary_urban', label: 'Secondary Urban Area',        color: '#8e44ad', dash: '8, 4'  },
];

const PUBLIC_SPACE_TYPES = [
    { key: 'garden', label: 'Garden', color: '#27ae60' },
    { key: 'square', label: 'Square', color: '#2980b9' },
];

const ROAD_TYPES = [
    { key: 'boulevard', label: 'Boulevard', category: 'primary'   },
    { key: 'avenue',    label: 'Avenue',    category: 'primary'   },
    { key: 'street',    label: 'Street',    category: 'secondary' },
    { key: 'drive',     label: 'Drive',     category: 'tertiary'  },
    { key: 'lane',      label: 'Lane',      category: 'tertiary'  },
    { key: 'cul_de_sac',label: 'Cul-de-sac',category: 'tertiary'  },
    { key: 'way',       label: 'Way',       category: 'tertiary'  },
];

const DISTRICT_TYPES = [
    { key: 'housing_estate', label: 'Housing Estate' },
    { key: 'urban_pole',     label: 'Urban Pole'     },
    { key: 'district',       label: 'District'       },
];

// ─── STATE ────────────────────────────────────────────────────────────────────

let currentPhase = 0;

let allFeatures = {
    areas: [], cityCenter: [], districts: [], roads: [],
    mainEntrances: [], secondaryEntrances: [], publicBuildings: [], publicSpaces: [],
};

let numberingConfig = {
    mode: null,           // null | 'city_center' | 'auto'
    cityCenter: null,     // { lat, lng }
};

let municipalityName   = '';
let municipalLimitRings = [];
let scatteredPolygons   = [];   // rings extracted from scattered GeoJSON for containment checks

// ─── LAYERS ───────────────────────────────────────────────────────────────────

const drawnItems        = new L.FeatureGroup().addTo(map);
const lineEndpointLayer = L.layerGroup().addTo(map);
const scatteredLayer    = L.layerGroup().addTo(map);
let   boundariesLayer   = null;
const POLYLINE_WEIGHT   = 8;

// ─── POLYGON STYLES ───────────────────────────────────────────────────────────

function areaStyle(areaTypeKey) {
    const at = AREA_TYPES.find(a => a.key === areaTypeKey) || AREA_TYPES[0];
    return { color: at.color, weight: 3, fillOpacity: 0.12, fillColor: at.color,
             ...(at.dash ? { dashArray: at.dash } : {}) };
}

const polygonStyles = {
    districts:       { color: '#f39c12', weight: 3, fillOpacity: 0.15, fillColor: '#f39c12' },
    publicBuildings: { color: '#e67e22', weight: 3, fillOpacity: 0.25, fillColor: '#e67e22' },
    publicSpaces:    { color: '#2ecc71', weight: 3, fillOpacity: 0.20, fillColor: '#2ecc71' },
};

const scatteredStyle = {
    color: '#7f8c8d', weight: 1.5, fillOpacity: 0.10, fillColor: '#7f8c8d',
    dashArray: '3, 6',
};

// ─── ICONS ───────────────────────────────────────────────────────────────────

function createEntranceIcon(label, color = '#27ae60') {
    const text = String(label || '').trim().slice(0, 6) || '?';
    return L.divIcon({
        className: 'entrance-marker',
        html: `<div style="background:${color};color:#fff;min-width:28px;height:28px;border-radius:14px;display:flex;align-items:center;justify-content:center;font-weight:700;font-size:10px;border:2px solid #fff;box-shadow:0 2px 5px rgba(0,0,0,0.35);padding:0 5px;">${text}</div>`,
        iconSize: [28, 28], iconAnchor: [14, 14], popupAnchor: [0, -14],
    });
}

function createCityCenterIcon() {
    return L.divIcon({
        className: 'city-center-marker',
        html: `<div style="background:#e74c3c;color:#fff;width:36px;height:36px;border-radius:50%;display:flex;align-items:center;justify-content:center;font-size:18px;border:3px solid #fff;box-shadow:0 2px 8px rgba(0,0,0,0.4);">★</div>`,
        iconSize: [36, 36], iconAnchor: [18, 18], popupAnchor: [0, -18],
    });
}

function createEndpointIcon(char, angleDeg, color, large = false) {
    const size = large ? 36 : 24, fontSize = large ? 28 : 20, half = size / 2;
    return L.divIcon({
        className: 'line-endpoint-marker',
        html: `<div style="color:${color};width:${size}px;height:${size}px;display:flex;align-items:center;justify-content:center;font-weight:900;font-size:${fontSize}px;line-height:1;text-shadow:-1px -1px 0 #fff,1px -1px 0 #fff,-1px 1px 0 #fff,1px 1px 0 #fff;transform:rotate(${angleDeg}deg);transform-origin:center;">${char}</div>`,
        iconSize: [size, size], iconAnchor: [half, half],
    });
}

function getSegmentAngle(a, b) {
    const fp = map.latLngToLayerPoint(a), tp = map.latLngToLayerPoint(b);
    return Math.atan2(tp.y - fp.y, tp.x - fp.x) * (180 / Math.PI);
}

function addPolylineEndpoints(layer) {
    if (!(layer instanceof L.Polyline) || layer instanceof L.Polygon) return;
    const lls = layer.getLatLngs();
    if (!lls || lls.length < 2) return;
    const c = layer.options.color || '#3498db';
    const s = L.marker(lls[0],              { icon: createEndpointIcon('>', getSegmentAngle(lls[0], lls[1]), c, true),  interactive: false });
    const e = L.marker(lls[lls.length - 1], { icon: createEndpointIcon('X', getSegmentAngle(lls[lls.length-2], lls[lls.length-1]), c, false), interactive: false });
    lineEndpointLayer.addLayer(s);
    lineEndpointLayer.addLayer(e);
    layer._endpointMarkers = [s, e];
}

// ─── LABELS ───────────────────────────────────────────────────────────────────

function createPermanentLabel(layer, label, phaseKey) {
    if (layer instanceof L.Marker) return;  // entrances show label in icon
    layer.bindTooltip(label, { permanent: true, direction: 'center', className: 'custom-shape-label' }).openTooltip();
}

// ─── SPATIAL HELPERS ─────────────────────────────────────────────────────────

function pointInRing(latlng, ring) {
    let inside = false;
    const x = latlng.lat, y = latlng.lng;
    for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) {
        const xi = ring[i].lat, yi = ring[i].lng, xj = ring[j].lat, yj = ring[j].lng;
        if (((yi > y) !== (yj > y)) && (x < (xj - xi) * (y - yi) / (yj - yi) + xi))
            inside = !inside;
    }
    return inside;
}

function pointInMunicipalLimit(latlng) {
    if (municipalLimitRings.length === 0) return true;
    return municipalLimitRings.some(r => pointInRing(latlng, r));
}

function pointInScatteredArea(latlng) {
    return scatteredPolygons.some(r => pointInRing(latlng, r));
}

function polylineMidpoint(layer) {
    const lls = layer.getLatLngs();
    return lls[Math.floor(lls.length / 2)];
}

function extractRings(geoJsonGeom) {
    const rings = [];
    if (!geoJsonGeom?.type || !geoJsonGeom?.coordinates) return rings;

    const processRing = coords => {
        if (!coords?.length) return;
        if (typeof coords[0][0] === 'number')
            rings.push(coords.map(c => L.latLng(c[1], c[0])));
        else
            coords.forEach(processRing);
    };

    if (geoJsonGeom.type === 'Polygon')        processRing(geoJsonGeom.coordinates);
    else if (geoJsonGeom.type === 'MultiPolygon') geoJsonGeom.coordinates.forEach(p => processRing(p));
    else processRing(geoJsonGeom.coordinates);

    return rings;
}

// ─── MUNICIPALITY BOUNDARY ────────────────────────────────────────────────────

async function displayCommuneBoundary(communeId, communeName) {
    try {
        if (boundariesLayer) { map.removeLayer(boundariesLayer); boundariesLayer = null; }
        const res = await apiFetch(`/api/commune/${communeId}/boundary`);
        if (!res.ok) return;
        const data = await res.json();
        let geojson = typeof data.geometry === 'string' ? JSON.parse(data.geometry) : data.geometry;
        if (!geojson?.type) return;

        municipalLimitRings = extractRings(geojson);
        boundariesLayer = L.geoJSON(geojson, {
            style: { color: '#e74c3c', weight: 2.5, fillOpacity: 0.03, fillColor: '#e74c3c' },
            onEachFeature(_, layer) {
                const name = communeName || data.commune_name;
                if (name) layer.bindTooltip(name, { permanent: false, direction: 'center', className: 'boundary-tooltip' });
            }
        }).addTo(map);
        map.fitBounds(boundariesLayer.getBounds(), { padding: [50, 50], maxZoom: 14 });
    } catch (e) { console.error('Boundary error:', e); }
}

// ─── SCATTERED AREA RENDERING ─────────────────────────────────────────────────

function renderScatteredAreas(geoJsonStr) {
    scatteredLayer.clearLayers();
    scatteredPolygons = [];
    if (!geoJsonStr) return;

    try {
        const geojson = typeof geoJsonStr === 'string' ? JSON.parse(geoJsonStr) : geoJsonStr;
        if (!geojson?.type) return;

        // Extract rings for containment checks
        scatteredPolygons = extractRings(geojson);

        // Render on map with hatch style
        L.geoJSON(geojson, {
            style: scatteredStyle,
            onEachFeature(_, layer) {
                layer.bindTooltip('Scattered Area', { direction: 'center', className: 'boundary-tooltip' });
            }
        }).addTo(scatteredLayer);
    } catch (e) { console.error('Scattered render error:', e); }
}

async function refreshScatteredAreas() {
    try {
        const res = await apiFetch('/api/areas/refresh-scattered', { method: 'POST' });
        if (!res.ok) return;
        const data = await res.json();
        if (data.geojson) renderScatteredAreas(data.geojson);
        else scatteredLayer.clearLayers();
    } catch (e) { console.error('Scatter refresh error:', e); }
}

// ─── PHASE BAR ────────────────────────────────────────────────────────────────

function injectPhaseUI() {
    const bar = document.createElement('div');
    bar.id = 'phaseBar';
    bar.innerHTML = '<div id="phaseSteps"></div>';
    document.body.insertBefore(bar, document.body.firstChild);
    renderPhaseBar();
}

function renderPhaseBar() {
    const el = document.getElementById('phaseSteps');
    if (!el) return;
    el.innerHTML = PHASES.map((p, i) => {
        const done = i < currentPhase, active = i === currentPhase;
        const cls  = done ? 'phase-step done' : active ? 'phase-step active' : 'phase-step locked';
        const badge = done ? '✓' : i + 1;
        const connector = i < PHASES.length - 1
            ? `<span class="phase-connector ${i < currentPhase ? 'done' : 'locked'}"></span>`
            : '';
        return `<button class="${cls}" title="${p.label}" onclick="goToPhase(${i})"><span class="phase-badge">${badge}</span></button>${connector}`;
    }).join('');
}

async function navigatePhase(direction) {
    const target = currentPhase + direction;
    if (target < 0 || target >= PHASES.length) return;

    if (direction > 0) {
        // ── Phase gate checks ─────────────────────────────────────────────────
        const from = PHASES[currentPhase];

        if (from.key === 'areas' && allFeatures.areas.length === 0) {
            alert('Please draw at least one urban area before proceeding.'); return;
        }

        if (from.key === 'cityCenter' && numberingConfig.mode === null) {
            alert('Please place a city center marker or click "Skip City Center Phase".'); return;
        }

        if (from.key === 'districts') {
            // Coverage gate: districts must fully cover all urban areas
            try {
                const res = await apiFetch('/api/validate/districts/coverage');
                const data = await res.json();
                if (!data.covered) { alert(`⛔ ${data.message}`); return; }
            } catch { alert('Could not verify district coverage. Please try again.'); return; }
        }

        if (from.key === 'roads' && allFeatures.roads.length === 0) {
            alert('Please draw at least one road before proceeding.'); return;
        }

        if (from.key === 'mainEntrances' && allFeatures.mainEntrances.length === 0) {
            alert('Please place at least one main entrance before proceeding.'); return;
        }
    }

    setPhase(target);
}

async function goToPhase(target) {
    if (target === currentPhase) return;
    if (target > currentPhase) {
        for (let i = currentPhase; i < target; i++) {
            currentPhase = i;
            await navigatePhase(1);
            if (currentPhase === i) return; // gate blocked
        }
    } else {
        setPhase(target);
    }
}

function setPhase(index) {
    currentPhase = index;
    buildDrawControl(PHASES[index]);
    renderPhaseBar();
    updateSkipButton();
    updateCounts();

    // Show city center dialog when entering that phase
    if (PHASES[index].key === 'cityCenter' && numberingConfig.mode === null)
        showCityCenterDialog();
}

// ─── CITY CENTER PHASE ────────────────────────────────────────────────────────

function showCityCenterDialog() {
    document.getElementById('cityCenterDialog').style.display = 'block';
}

function updateSkipButton() {
    const btn = document.getElementById('skipCityCenterBtn');
    btn.style.display = PHASES[currentPhase].key === 'cityCenter' ? 'block' : 'none';
}

document.getElementById('cityCenterYes').addEventListener('click', () => {
    document.getElementById('cityCenterDialog').style.display = 'none';
    // User will now place the marker — draw tool is already active
});

document.getElementById('cityCenterNo').addEventListener('click', () => {
    document.getElementById('cityCenterDialog').style.display = 'none';
    numberingConfig.mode = 'auto';
    setPhase(2); // Skip to Districts
});

document.getElementById('skipCityCenterBtn').addEventListener('click', () => {
    numberingConfig.mode = 'auto';
    setPhase(2);
});

// ─── DRAW CONTROL ─────────────────────────────────────────────────────────────

let drawControl = null;

function buildDrawControl(phase) {
    if (drawControl) { map.removeControl(drawControl); drawControl = null; }
    const opts = { polygon: false, polyline: false, rectangle: false, circle: false, circlemarker: false, marker: false };
    if (phase.drawType === 'polygon')  opts.polygon  = { allowIntersection: false, shapeOptions: { color: phase.color, weight: 3, fillOpacity: 0.15 } };
    if (phase.drawType === 'polyline') opts.polyline = { shapeOptions: { color: phase.color, weight: POLYLINE_WEIGHT } };
    if (phase.drawType === 'marker') {
        const icon = phase.key === 'cityCenter'
            ? createCityCenterIcon()
            : createEntranceIcon('?', phase.color);
        opts.marker = { icon };
    }
    drawControl = new L.Control.Draw({ edit: { featureGroup: drawnItems, edit: true, remove: true }, draw: opts });
    map.addControl(drawControl);
}

// ─── VALIDATION HELPERS ───────────────────────────────────────────────────────

async function validateRoad(layer) {
    const coords = layer.getLatLngs().map(ll => ({ lat: ll.lat, lng: ll.lng }));
    try {
        const res = await apiFetch('/api/validate/road', {
            method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ coordinates: coords }),
        });
        if (!res.ok) return { valid: false, error: 'Road validation request failed.' };
        return await res.json();
    } catch { return { valid: false, error: 'Cannot reach validation service.' }; }
}

async function validateDistrict(layer) {
    const coords = layer.getLatLngs()[0].map(ll => ({ lat: ll.lat, lng: ll.lng }));
    try {
        const res = await apiFetch('/api/validate/district', {
            method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ coordinates: coords }),
        });
        if (!res.ok) return { valid: false, error: 'District validation request failed.' };
        return await res.json();
    } catch { return { valid: false, error: 'Cannot reach validation service.' }; }
}

async function getRoadSide(roadDbId, lat, lng) {
    try {
        const res = await apiFetch('/api/road-side', {
            method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ roadId: roadDbId, lat, lng }),
        });
        if (!res.ok) return null;
        return await res.json();  // { side, suggestedNumber }
    } catch { return null; }
}

async function checkMainUrbanExists() {
    try {
        const res = await apiFetch('/api/validate/area/main-urban-exists');
        if (!res.ok) return false;
        const d = await res.json();
        return d.exists;
    } catch { return false; }
}

// ─── PLACEMENT VALIDATION ─────────────────────────────────────────────────────

async function validatePlacement(layer, phase) {
    // ── 1. Inside municipal limit ─────────────────────────────────────────────
    let checkPoint = phase.drawType === 'marker' ? layer.getLatLng()
                   : phase.drawType === 'polyline' ? polylineMidpoint(layer)
                   : layer.getBounds().getCenter();

    if (!pointInMunicipalLimit(checkPoint)) {
        alert(`⛔ This ${phase.label.slice(0, -1).toLowerCase()} is outside the municipal boundary.\nDrawing outside the municipal limit is not allowed.`);
        return false;
    }

    // ── 2. Scattered area restriction ─────────────────────────────────────────
    // Public buildings are allowed everywhere. Areas phase draws urban areas (no check).
    if (phase.key !== 'publicBuildings' && phase.key !== 'areas' && phase.key !== 'cityCenter') {
        if (pointInScatteredArea(checkPoint)) {
            alert(`⛔ This ${phase.label.slice(0, -1).toLowerCase()} cannot be placed in a scattered area.\nOnly public buildings are allowed in scattered areas.`);
            return false;
        }
    }

    return true;
}

// ─── MODAL ────────────────────────────────────────────────────────────────────

function showModal(phase, layer, callback) {
    const modal            = document.getElementById('labelModal');
    const header           = document.getElementById('modalHeader');
    const hint             = document.getElementById('modalHint');
    const labelInput       = document.getElementById('labelInput');
    const decisionNoInput  = document.getElementById('decisionNumberInput');
    const decisionDtInput  = document.getElementById('decisionDateInput');
    const extras           = document.getElementById('modalExtras');
    const saveBtn          = document.getElementById('saveLabel');
    const cancelBtn        = document.getElementById('cancelLabel');

    header.textContent = `Add ${phase.label.slice(0, -1)} Details`;
    hint.textContent   = phase.hint;

    // Reset fields
    labelInput.value      = '';
    decisionNoInput.value = '';
    decisionDtInput.value = '';
    labelInput.classList.remove('error');
    decisionNoInput.classList.remove('error');
    decisionDtInput.classList.remove('error');
    extras.innerHTML = '';

    // ── Phase-specific extras ─────────────────────────────────────────────────
    let extraState = {};

    if (phase.key === 'areas') {
        buildAreaExtras(extras, extraState);
    } else if (phase.key === 'districts') {
        buildDistrictExtras(extras, extraState);
    } else if (phase.key === 'roads') {
        buildRoadExtras(extras, extraState);
    } else if (phase.key === 'mainEntrances') {
        buildMainEntranceExtras(extras, extraState, layer);
    } else if (phase.key === 'secondaryEntrances') {
        buildSecondaryEntranceExtras(extras, extraState);
    } else if (phase.key === 'publicSpaces') {
        buildPublicSpaceExtras(extras, extraState);
    }

    // Auto-fill municipality name for main urban area
    if (phase.key === 'areas') {
        checkMainUrbanExists().then(exists => {
            const sel = document.getElementById('areaTypeSelector');
            if (!sel) return;
            // Remove central_urban option if already exists
            if (exists) {
                const opt = sel.querySelector('option[value="central_urban"]');
                if (opt) opt.remove();
                sel.value = 'secondary_urban';
            } else {
                // Pre-fill label with municipality name for main urban
                if (municipalityName) labelInput.value = municipalityName;
                sel.value = 'central_urban';
            }
        });
    }

    modal.style.display = 'block';
    labelInput.focus();

    const handleSave = async () => {
        const label      = labelInput.value.trim();
        const decisionNo = decisionNoInput.value.trim();
        const decisionDt = decisionDtInput.value.trim();

        // Validate required fields
        let valid = true;
        if (!label)      { labelInput.classList.add('error');      valid = false; }
        if (!decisionNo) { decisionNoInput.classList.add('error'); valid = false; }
        if (!decisionDt) { decisionDtInput.classList.add('error'); valid = false; }
        if (!valid) return;

        // Collect extra state
        const extra = collectExtras(phase, extraState, layer);
        if (!extra.valid) return;

        cleanup();
        callback({ label, decisionNumber: decisionNo, decisionDate: decisionDt, ...extra.data });
    };

    const handleCancel = () => { cleanup(); callback(null); };
    const cleanup = () => {
        modal.style.display = 'none';
        saveBtn.removeEventListener('click', handleSave);
        cancelBtn.removeEventListener('click', handleCancel);
        document.removeEventListener('keyup', handleKey);
    };
    const handleKey = e => { if (e.key === 'Enter') handleSave(); if (e.key === 'Escape') handleCancel(); };

    saveBtn.addEventListener('click', handleSave);
    cancelBtn.addEventListener('click', handleCancel);
    document.addEventListener('keyup', handleKey);
}

// ── Area extras ───────────────────────────────────────────────────────────────
function buildAreaExtras(container, state) {
    state.areaTypeKey = 'central_urban';
    const opts = AREA_TYPES.map(a => `<option value="${a.key}">${a.label}</option>`).join('');
    container.innerHTML = `
        <div class="modal-field">
            <label>Area Type <span class="req">*</span></label>
            <select id="areaTypeSelector" class="modal-input">
                ${opts}
            </select>
        </div>`;
    document.getElementById('areaTypeSelector').addEventListener('change', e => {
        state.areaTypeKey = e.target.value;
        // Auto-fill name for main urban
        const labelInput = document.getElementById('labelInput');
        if (state.areaTypeKey === 'central_urban' && municipalityName && !labelInput.value)
            labelInput.value = municipalityName;
    });
}

// ── District extras ───────────────────────────────────────────────────────────
function buildDistrictExtras(container, state) {
    state.districtTypeKey = 'district';
    const opts = DISTRICT_TYPES.map(d => `<option value="${d.key}">${d.label}</option>`).join('');
    container.innerHTML = `
        <div class="modal-field">
            <label>District Type <span class="req">*</span></label>
            <select id="districtTypeSelector" class="modal-input">
                ${opts}
            </select>
        </div>`;
    document.getElementById('districtTypeSelector').addEventListener('change', e => {
        state.districtTypeKey = e.target.value;
    });
}

// ── Road extras ───────────────────────────────────────────────────────────────
function buildRoadExtras(container, state) {
    state.roadTypeKey = 'street';
    const opts = ROAD_TYPES.map(r => `<option value="${r.key}">${r.label}</option>`).join('');
    container.innerHTML = `
        <div class="modal-field">
            <label>Road Type <span class="req">*</span></label>
            <select id="roadTypeSelector" class="modal-input">
                ${opts}
            </select>
        </div>`;
    document.getElementById('roadTypeSelector').addEventListener('change', e => {
        state.roadTypeKey = e.target.value;
    });
}

// ── Main entrance extras ──────────────────────────────────────────────────────
function buildMainEntranceExtras(container, state, layer) {
    state.roadDbId    = null;
    state.roadLabel   = null;
    state.side        = null;
    state.entranceNum = null;

    const roadOpts = allFeatures.roads
        .map((r, i) => `<option value="${i}">${r.data.label || 'Road ' + (i+1)}</option>`)
        .join('');

    container.innerHTML = `
        <div class="modal-field">
            <label>Assign to Road <span class="req">*</span></label>
            <select id="roadSelectorForEntrance" class="modal-input">
                <option value="">— Select a road —</option>
                ${roadOpts}
            </select>
        </div>
        <div class="modal-field" id="entranceNumberField" style="display:none;">
            <label>Entrance Number <span id="sideHint" style="font-weight:400;text-transform:none;color:#888;"></span></label>
            <div style="display:flex;align-items:center;gap:8px;">
                <input type="number" id="entranceNumberInput" class="modal-input" style="width:100px;" min="1">
                <span id="entranceSideSpinner" class="field-spinner" style="display:none;"></span>
                <span id="entranceSideLabel" style="font-size:12px;color:#888;"></span>
            </div>
        </div>`;

    document.getElementById('roadSelectorForEntrance').addEventListener('change', async e => {
        const idx = parseInt(e.target.value);
        if (isNaN(idx)) { state.roadDbId = null; return; }

        const roadEntry = allFeatures.roads[idx];
        state.roadDbId  = roadEntry?.layer?._dbId ?? null;
        state.roadLabel = roadEntry?.data?.label ?? null;

        if (!state.roadDbId) { document.getElementById('entranceNumberField').style.display = 'none'; return; }

        // Fetch road side and suggested number
        const spinner   = document.getElementById('entranceSideSpinner');
        const sideLabel = document.getElementById('entranceSideLabel');
        const numField  = document.getElementById('entranceNumberField');
        const numInput  = document.getElementById('entranceNumberInput');
        const sideHint  = document.getElementById('sideHint');

        spinner.style.display = 'inline-block';
        numField.style.display = 'block';
        sideLabel.textContent  = 'Detecting side…';

        const ll     = layer.getLatLng();
        const result = await getRoadSide(state.roadDbId, ll.lat, ll.lng);
        spinner.style.display = 'none';

        if (result) {
            state.side        = result.side;
            state.entranceNum = result.suggestedNumber;
            const sideText    = result.side === 'left' ? 'Left side (odd numbers)' : 'Right side (even numbers)';
            sideLabel.textContent = sideText;
            sideHint.textContent  = `— ${sideText}`;
            numInput.value        = result.suggestedNumber;
            numInput.addEventListener('change', () => { state.entranceNum = parseInt(numInput.value); });
        } else {
            sideLabel.textContent = 'Could not determine side — please enter number manually.';
            numInput.value = '';
        }
    });
}

// ── Secondary entrance extras ─────────────────────────────────────────────────
function buildSecondaryEntranceExtras(container, state) {
    state.mainEntranceDbId = null;
    state.mainEntranceLabel = null;
    state.bisNumber = null;

    const mainOpts = allFeatures.mainEntrances
        .map((m, i) => `<option value="${i}">${m.data.label || 'Entrance ' + (i+1)}</option>`)
        .join('');

    container.innerHTML = `
        <div class="modal-field">
            <label>Assign to Main Entrance <span class="req">*</span></label>
            <select id="mainEntranceSelector" class="modal-input">
                <option value="">— Select main entrance —</option>
                ${mainOpts}
            </select>
        </div>
        <div class="modal-field" id="bisNumberField" style="display:none;">
            <label>BIS Number (auto-suggested)</label>
            <input type="text" id="bisNumberInput" class="modal-input" readonly
                   style="cursor:default;opacity:0.7;" placeholder="e.g. BIS01">
        </div>`;

    document.getElementById('mainEntranceSelector').addEventListener('change', e => {
        const idx = parseInt(e.target.value);
        if (isNaN(idx)) { state.mainEntranceDbId = null; return; }

        const entry = allFeatures.mainEntrances[idx];
        state.mainEntranceDbId  = entry?.layer?._dbId ?? null;
        state.mainEntranceLabel = entry?.data?.label ?? null;

        // Count existing secondary entrances for this main entrance
        const count = allFeatures.secondaryEntrances.filter(s =>
            s.data.mainEntranceDbId === state.mainEntranceDbId).length;

        state.bisNumber = count + 1;
        const bisStr = 'BIS' + String(state.bisNumber).padStart(2, '0');
        document.getElementById('bisNumberField').style.display = 'block';
        document.getElementById('bisNumberInput').value = bisStr;
        // Also pre-fill the label
        document.getElementById('labelInput').value = bisStr;
    });
}

// ── Public space extras ───────────────────────────────────────────────────────
function buildPublicSpaceExtras(container, state) {
    state.spaceTypeKey = 'garden';
    const opts = PUBLIC_SPACE_TYPES.map(s => `<option value="${s.key}">${s.label}</option>`).join('');
    container.innerHTML = `
        <div class="modal-field">
            <label>Space Type <span class="req">*</span></label>
            <select id="spaceTypeSelector" class="modal-input">
                ${opts}
            </select>
        </div>`;
    document.getElementById('spaceTypeSelector').addEventListener('change', e => {
        state.spaceTypeKey = e.target.value;
    });
}

// ── Collect extras values ─────────────────────────────────────────────────────
function collectExtras(phase, state, layer) {
    switch (phase.key) {
        case 'areas': {
            const sel = document.getElementById('areaTypeSelector');
            if (!sel) return { valid: false };
            return { valid: true, data: { areaTypeKey: sel.value } };
        }
        case 'districts': {
            const sel = document.getElementById('districtTypeSelector');
            if (!sel) return { valid: false };
            return { valid: true, data: { districtTypeKey: sel.value } };
        }
        case 'roads': {
            const sel = document.getElementById('roadTypeSelector');
            if (!sel) return { valid: false };
            return { valid: true, data: { roadTypeKey: sel.value } };
        }
        case 'mainEntrances': {
            if (!state.roadDbId) {
                const sel = document.getElementById('roadSelectorForEntrance');
                if (sel) sel.style.borderColor = '#e74c3c';
                return { valid: false };
            }
            const ll = layer.getLatLng();
            return { valid: true, data: {
                roadDbId: state.roadDbId, roadLabel: state.roadLabel,
                side: state.side, entranceNumber: state.entranceNum,
                lat: ll.lat, lng: ll.lng,
            }};
        }
        case 'secondaryEntrances': {
            if (!state.mainEntranceDbId) {
                const sel = document.getElementById('mainEntranceSelector');
                if (sel) sel.style.borderColor = '#e74c3c';
                return { valid: false };
            }
            const ll = layer.getLatLng();
            return { valid: true, data: {
                mainEntranceDbId: state.mainEntranceDbId, mainEntranceLabel: state.mainEntranceLabel,
                bisNumber: state.bisNumber, lat: ll.lat, lng: ll.lng,
            }};
        }
        case 'publicSpaces': {
            const sel = document.getElementById('spaceTypeSelector');
            if (!sel) return { valid: false };
            return { valid: true, data: { spaceTypeKey: sel.value } };
        }
        default:
            return { valid: true, data: {} };
    }
}

// ─── FEATURE DATA BUILDER ─────────────────────────────────────────────────────

function buildFeatureData(layer, phase, modalResult) {
    const base = {
        type:           phase.key,
        label:          modalResult.label,
        decisionNumber: modalResult.decisionNumber,
        decisionDate:   modalResult.decisionDate,
        ...modalResult,  // spread all extra fields
    };

    if (phase.drawType === 'marker') {
        const ll = layer.getLatLng();
        return { ...base, lat: ll.lat, lng: ll.lng };
    }
    const lls = phase.drawType === 'polygon' ? layer.getLatLngs()[0] : layer.getLatLngs();
    return { ...base, coordinates: lls.map(ll => ({ lat: ll.lat, lng: ll.lng })) };
}

// ─── API SAVE SHAPE ───────────────────────────────────────────────────────────

function toApiSaveShape(featureData) {
    switch (featureData.type) {
        case 'areas':              return { type: 'area',            layer: featureData.areaTypeKey     || 'central_urban' };
        case 'cityCenter':         return { type: 'city_center',     layer: 'city_center' };
        case 'districts':          return { type: 'district',        layer: featureData.districtTypeKey || 'district' };
        case 'roads':              return { type: 'road',            layer: featureData.roadTypeKey     || 'street' };
        case 'mainEntrances':      return { type: 'house_entrance',  layer: 'main_entrance' };
        case 'secondaryEntrances': return { type: 'house_entrance',  layer: 'secondary_entrance' };
        case 'publicBuildings':    return { type: 'public_building', layer: 'public_building' };
        case 'publicSpaces':       return { type: 'public_space',    layer: featureData.spaceTypeKey    || 'garden' };
        default: return null;
    }
}

// ─── DATABASE SAVE / LOAD ─────────────────────────────────────────────────────

async function saveToDatabase(featureData) {
    try {
        const shape = toApiSaveShape(featureData);
        if (!shape) return { ok: false, error: `Unknown feature type '${featureData.type}'.` };

        const res = await apiFetch('/api/save', {
            method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ type: shape.type, layer: shape.layer, label: featureData.label, data: featureData }),
        });

        if (!res.ok) {
            const raw = await res.text();
            let detail = raw || `HTTP ${res.status}`;
            try { const p = JSON.parse(raw); detail = p?.detail || p?.title || p?.message || detail; } catch {}
            return { ok: false, error: `HTTP ${res.status}: ${String(detail).slice(0, 240)}` };
        }
        return { ok: true, data: await res.json() };
    } catch (err) {
        return { ok: false, error: err?.message || 'Network error' };
    }
}

async function loadFromDatabase() {
    try {
        const res = await apiFetch('/api/load');
        if (!res.ok) { console.error('Load failed:', res.status); return; }
        const features = await res.json();
        if (!features.length) { console.log('No saved features.'); return; }

        drawnItems.clearLayers();
        lineEndpointLayer.clearLayers();
        allFeatures = { areas: [], cityCenter: [], districts: [], roads: [],
                        mainEntrances: [], secondaryEntrances: [], publicBuildings: [], publicSpaces: [] };

        let loaded = 0, skipped = 0;

        features.forEach(feature => {
            try {
                const data = typeof feature.data === 'string' ? JSON.parse(feature.data) : feature.data;

                // Handle scattered areas separately
                if (feature.layer === 'scattered') {
                    if (data.geometry) renderScatteredAreas(data.geometry);
                    return;
                }

                // Resolve phase key
                let phaseKey = API_LAYER_TO_PHASE[feature.layer] || data.type;
                if (!phaseKey || !allFeatures.hasOwnProperty(phaseKey)) { skipped++; return; }

                const phase = PHASES.find(p => p.key === phaseKey);
                if (!phase) { skipped++; return; }

                let layer;
                if (phase.drawType === 'marker') {
                    if (!data.lat || !data.lng) { skipped++; return; }
                    const icon = phase.key === 'cityCenter' ? createCityCenterIcon()
                               : createEntranceIcon(data.label, phase.color);
                    layer = L.marker([data.lat, data.lng], { icon });

                    // Restore city center state
                    if (phase.key === 'cityCenter') {
                        numberingConfig.mode       = 'city_center';
                        numberingConfig.cityCenter = { lat: data.lat, lng: data.lng };
                    }
                } else if (phase.drawType === 'polyline') {
                    if (!data.coordinates?.length) { skipped++; return; }
                    layer = L.polyline(data.coordinates.map(c => [c.lat, c.lng]),
                        { color: phase.color, weight: POLYLINE_WEIGHT });
                } else {
                    if (!data.coordinates?.length) { skipped++; return; }
                    const style = phase.key === 'areas'
                        ? areaStyle(data.areaTypeKey || feature.layer)
                        : (polygonStyles[phaseKey] || { color: phase.color, weight: 3, fillOpacity: 0.15 });
                    layer = L.polygon(data.coordinates.map(c => [c.lat, c.lng]), style);
                }

                layer._dbId = feature.id;
                drawnItems.addLayer(layer);
                if (phase.drawType === 'polyline') addPolylineEndpoints(layer);
                createPermanentLabel(layer, data.label, phaseKey);
                layer.bindPopup(buildPopup(data, phase));

                allFeatures[phaseKey].push({ layer, data });
                loaded++;
            } catch (err) { console.error('Load feature error:', err); skipped++; }
        });

        // Resume at the furthest phase with data
        for (let i = PHASES.length - 1; i >= 0; i--) {
            if (allFeatures[PHASES[i].key].length > 0) { currentPhase = i; break; }
        }
        // If city center was skipped, set auto mode
        if (currentPhase >= 2 && numberingConfig.mode === null)
            numberingConfig.mode = 'auto';

        buildDrawControl(PHASES[currentPhase]);
        updateCounts();
        console.log(`✓ Loaded ${loaded} features (${skipped} skipped)`);
    } catch (err) { console.error('Load error:', err); }
}

// ─── POPUP BUILDER ────────────────────────────────────────────────────────────

function buildPopup(data, phase) {
    const lines = [`<b>${data.label}</b>`, `<small>${phase.label}</small>`];
    if (data.decisionNumber) lines.push(`<small>Decision: ${data.decisionNumber}</small>`);
    if (data.decisionDate)   lines.push(`<small>Date: ${data.decisionDate}</small>`);
    if (data.roadLabel)      lines.push(`<small>Road: ${data.roadLabel}</small>`);
    if (data.side)           lines.push(`<small>Side: ${data.side} (${data.side === 'left' ? 'odd' : 'even'})</small>`);
    if (data.mainEntranceLabel) lines.push(`<small>Main entrance: ${data.mainEntranceLabel}</small>`);
    return lines.join('<br>');
}

// ─── COUNTS ───────────────────────────────────────────────────────────────────

function updateCounts() {
    const setEl = (id, val) => { const el = document.getElementById(id); if (el) el.textContent = val; };
    setEl('areasCount',             allFeatures.areas.length);
    setEl('cityCenterCount',        allFeatures.cityCenter.length > 0 ? 'Placed' : (numberingConfig.mode === 'auto' ? 'Skipped' : '—'));
    setEl('districtsCount',         allFeatures.districts.length);
    setEl('roadsCount',             allFeatures.roads.length);
    setEl('mainEntrancesCount',     allFeatures.mainEntrances.length);
    setEl('secondaryEntrancesCount',allFeatures.secondaryEntrances.length);
    setEl('publicBuildingsCount',   allFeatures.publicBuildings.length);
    setEl('publicSpacesCount',      allFeatures.publicSpaces.length);
    renderPhaseBar();
}

// ─── DRAW EVENTS ─────────────────────────────────────────────────────────────

map.on(L.Draw.Event.CREATED, async function (event) {
    const layer = event.layer;
    const phase = PHASES[currentPhase];

    // ── Placement validation ──────────────────────────────────────────────────
    const placementOk = await validatePlacement(layer, phase);
    if (!placementOk) return;

    // ── Phase-specific pre-modal validation ──────────────────────────────────
    if (phase.key === 'roads') {
        const roadCheck = await validateRoad(layer);
        if (!roadCheck.valid) { alert(`⛔ Road cannot be saved:\n${roadCheck.error}`); return; }
    }

    if (phase.key === 'districts') {
        const districtCheck = await validateDistrict(layer);
        if (!districtCheck.valid) { alert(`⛔ District cannot be saved:\n${districtCheck.error}`); return; }
    }

    // ── Show modal ────────────────────────────────────────────────────────────
    showModal(phase, layer, async (modalResult) => {
        if (!modalResult) return;

        // Apply visual style
        if (phase.key === 'areas')          layer.setStyle(areaStyle(modalResult.areaTypeKey));
        else if (phase.key === 'districts') layer.setStyle(polygonStyles.districts);
        else if (phase.key === 'publicBuildings') layer.setStyle(polygonStyles.publicBuildings);
        else if (phase.key === 'publicSpaces') layer.setStyle(polygonStyles.publicSpaces);
        else if (phase.drawType === 'polyline') layer.setStyle({ color: phase.color, weight: POLYLINE_WEIGHT });
        else if (phase.key === 'mainEntrances') {
            layer.setIcon(createEntranceIcon(String(modalResult.entranceNumber || modalResult.label), phase.color));
        } else if (phase.key === 'secondaryEntrances') {
            const bisStr = 'BIS' + String(modalResult.bisNumber || 1).padStart(2, '0');
            layer.setIcon(createEntranceIcon(bisStr, phase.color));
        } else if (phase.key === 'cityCenter') {
            layer.setIcon(createCityCenterIcon());
        }

        const featureData = buildFeatureData(layer, phase, modalResult);
        const saveResult  = await saveToDatabase(featureData);

        if (!saveResult.ok) {
            alert(`Failed to save feature.\n${saveResult.error || 'Please try again.'}`);
            return;
        }

        layer._dbId = saveResult.data.id;
        drawnItems.addLayer(layer);
        if (phase.drawType === 'polyline') addPolylineEndpoints(layer);
        createPermanentLabel(layer, modalResult.label, phase.key);
        layer.bindPopup(buildPopup(featureData, phase));

        allFeatures[phase.key].push({ layer, data: featureData });

        // Handle city center state
        if (phase.key === 'cityCenter') {
            const ll = layer.getLatLng();
            numberingConfig.mode       = 'city_center';
            numberingConfig.cityCenter = { lat: ll.lat, lng: ll.lng };
            // Advance to districts automatically
            setTimeout(() => setPhase(2), 400);
        }

        // Recompute scattered areas after any urban area change
        if (phase.key === 'areas') await refreshScatteredAreas();

        updateCounts();
    });
});

map.on(L.Draw.Event.EDITED, async function (event) {
    event.layers.eachLayer(async (layer) => {
        if (!layer._dbId) return;
        try {
            const phase = PHASES.find(p => allFeatures[p.key].some(f => f.layer === layer));
            const entry = phase ? allFeatures[phase.key].find(f => f.layer === layer) : null;
            if (!entry) return;

            if (layer instanceof L.Marker) {
                const ll = layer.getLatLng();
                entry.data.lat = ll.lat; entry.data.lng = ll.lng;
            } else if (layer instanceof L.Polyline && !(layer instanceof L.Polygon)) {
                entry.data.coordinates = layer.getLatLngs().map(ll => ({ lat: ll.lat, lng: ll.lng }));
            } else if (layer instanceof L.Polygon) {
                entry.data.coordinates = layer.getLatLngs()[0].map(ll => ({ lat: ll.lat, lng: ll.lng }));
            }

            await apiFetch(`/api/update/${layer._dbId}`, {
                method: 'PUT', headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ data: entry.data }),
            });

            if (phase?.key === 'areas') await refreshScatteredAreas();
        } catch (err) { console.error('Edit persist error:', err); }
    });

    lineEndpointLayer.clearLayers();
    drawnItems.eachLayer(l => { if (l instanceof L.Polyline && !(l instanceof L.Polygon)) addPolylineEndpoints(l); });
});

map.on(L.Draw.Event.DELETED, async function (event) {
    let areaDeleted = false;
    event.layers.eachLayer(async (layer) => {
        if (layer._dbId) {
            try {
                const res = await apiFetch(`/api/delete/${layer._dbId}`, { method: 'DELETE' });
                if (!res.ok) console.error(`Delete failed: ${layer._dbId}`, res.status);

                // Check if it was an area
                if (allFeatures.areas.some(f => f.layer === layer)) areaDeleted = true;
            } catch (err) { console.error('Delete error:', err); }
        }
        if (layer._endpointMarkers) layer._endpointMarkers.forEach(m => lineEndpointLayer.removeLayer(m));
    });

    lineEndpointLayer.clearLayers();
    drawnItems.eachLayer(l => { if (l instanceof L.Polyline && !(l instanceof L.Polygon)) addPolylineEndpoints(l); });

    for (const key of Object.keys(allFeatures))
        allFeatures[key] = allFeatures[key].filter(f => drawnItems.hasLayer(f.layer));

    if (areaDeleted) await refreshScatteredAreas();
    updateCounts();
});

// ─── COMMUNE NAVIGATION ───────────────────────────────────────────────────────

async function navigateToUserCommune() {
    try {
        const res = await apiFetch('/api/current_user');
        if (!res.ok) return;
        const user = await res.json();
        municipalityName = user.commune?.name_fr || '';

        if (user.commune?.id)
            await displayCommuneBoundary(user.commune.id, user.commune.name_fr);
        if (user.commune?.latitude && user.commune?.longitude)
            map.setView([parseFloat(user.commune.latitude), parseFloat(user.commune.longitude)], 13);
    } catch (err) { console.error('Commune nav error:', err); }
}

// ─── PROFILE UI ───────────────────────────────────────────────────────────────

async function loadUserProfile() {
    try {
        const res = await apiFetch('/api/current_user');
        if (!res.ok) return;
        const user = await res.json();
        const setEl = (id, val) => { const el = document.getElementById(id); if (el) el.textContent = val; };
        setEl('profileUsername', user.username);
        setEl('profileName',     user.name);
        const icon = document.getElementById('profileIcon');
        if (icon) icon.textContent = user.username.charAt(0).toUpperCase();
    } catch (err) { console.error('Profile error:', err); }
}

const profileButton   = document.getElementById('profileButton');
const profileDropdown = document.getElementById('profileDropdown');
const dropdownArrow   = document.getElementById('dropdownArrow');

if (profileButton) {
    profileButton.addEventListener('click', e => {
        e.stopPropagation();
        profileDropdown?.classList.toggle('show');
        dropdownArrow?.classList.toggle('open');
    });
    document.addEventListener('click', e => {
        if (!profileButton.contains(e.target) && !profileDropdown?.contains(e.target)) {
            profileDropdown?.classList.remove('show');
            dropdownArrow?.classList.remove('open');
        }
    });
}

document.getElementById('settingsItem')?.addEventListener('click', () => {
    alert('Settings coming soon.'); profileDropdown?.classList.remove('show');
});

document.getElementById('logoutItem')?.addEventListener('click', async () => {
    try {
        const res = await apiFetch('/api/logout', { method: 'POST', headers: { 'Content-Type': 'application/json' } });
        if (res.ok) window.location.href = '/login';
        else alert('Logout failed. Please try again.');
    } catch { alert('Logout failed. Please try again.'); }
});

// ─── STYLES ───────────────────────────────────────────────────────────────────

const appStyle = document.createElement('style');
appStyle.textContent = `
#phaseBar { position:fixed;top:10px;left:50%;transform:translateX(-50%);z-index:900;pointer-events:none; }
#phaseSteps { display:flex;align-items:center;pointer-events:auto; }
.phase-step {
    width:30px;height:30px;padding:0;border:1.5px solid transparent;border-radius:50%;
    display:inline-flex;align-items:center;justify-content:center;
    background:transparent;cursor:pointer;transition:all 0.2s;
}
.phase-step.done   { background:rgba(255,255,255,0.78);border-color:#a8a8a8;color:#3f3f3f; }
.phase-step.active { background:rgba(243,243,243,0.88);border-color:#555;color:#1f1f1f; }
.phase-step.locked { background:rgba(220,220,220,0.6);border-color:#bbb;color:#888; }
.phase-badge { width:18px;height:18px;border-radius:50%;display:flex;align-items:center;justify-content:center;font-size:10px;font-weight:700;background:currentColor;color:#fff; }
.phase-step.locked .phase-badge { background:#b0b0b0; }
.phase-connector { width:40px;height:2px;border-radius:999px; }
.phase-connector.done   { background:rgba(255,255,255,0.8); }
.phase-connector.locked { background:rgba(255,255,255,0.2); }
#map { margin-top:0!important;height:100vh!important; }
.custom-shape-label {
    background:transparent!important;border:none!important;box-shadow:none!important;
    font-weight:700!important;font-size:13px!important;color:#fff!important;
    text-shadow:-1px -1px 0 #000,1px -1px 0 #000,-1px 1px 0 #000,1px 1px 0 #000!important;
    padding:4px 8px!important;
}
.custom-shape-label::before { display:none!important; }
.boundary-tooltip {
    background:rgba(231,76,60,0.9)!important;border:2px solid #fff!important;border-radius:4px!important;
    color:#fff!important;font-weight:700!important;font-size:12px!important;padding:4px 10px!important;
    box-shadow:0 2px 5px rgba(0,0,0,0.3)!important;
}
`;
document.head.appendChild(appStyle);

// ─── BOOTSTRAP ────────────────────────────────────────────────────────────────

window.addEventListener('DOMContentLoaded', async () => {
    injectPhaseUI();
    buildDrawControl(PHASES[currentPhase]);
    await navigateToUserCommune();
    await loadFromDatabase();
    loadUserProfile();
    updateCounts();
    updateSkipButton();
    console.log('NARS Urban Addressing — initialized');
});
