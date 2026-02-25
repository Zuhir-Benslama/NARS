// Initialize map centered on Algiers, Algeria
const map = L.map('map').setView([36.7538, 3.0588], 10);

// Define base layers (different map views)
const satelliteLayer = L.tileLayer('https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}', {
    attribution: 'Tiles &copy; Esri &mdash; Source: Esri, i-cubed, USDA, USGS, AEX, GeoEye, Getmapping, Aerogrid, IGN, IGP, UPR-EGP, and the GIS User Community'
});

const streetLayer = L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
    attribution: '© OpenStreetMap contributors'
});

const topoLayer = L.tileLayer('https://{s}.tile.opentopomap.org/{z}/{x}/{y}.png', {
    attribution: 'Map data: © OpenStreetMap contributors, SRTM | Map style: © OpenTopoMap'
});

const cartoLayer = L.tileLayer('https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png', {
    attribution: '© OpenStreetMap contributors © CARTO',
    subdomains: 'abcd'
});

const darkLayer = L.tileLayer('https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png', {
    attribution: '© OpenStreetMap contributors © CARTO',
    subdomains: 'abcd'
});

// Add default layer (satellite)
satelliteLayer.addTo(map);

// GeoJSON boundaries layer
let boundariesLayer = null;

// Function to display a specific commune boundary from database
// Fetch and display commune boundary from database
async function displayCommuneBoundary(communeId, communeName) {
    try {
        // Remove existing boundaries if any
        if (boundariesLayer) {
            map.removeLayer(boundariesLayer);
            boundariesLayer = null;
        }
        
        console.log(`Fetching boundary for commune ID: ${communeId}`);
        const response = await fetch(`/api/commune/${communeId}/boundary`);
        
        if (!response.ok) {
            console.error(`Failed to fetch boundary: ${response.status}`);
            return;
        }
        
        const boundaryData = await response.json();
        console.log('Boundary data received:', boundaryData);
        console.log('Geometry type:', typeof boundaryData.geometry);
        console.log('Geometry preview:', boundaryData.geometry?.substring(0, 100));
        
        // Parse geometry
        let geojson;
        if (typeof boundaryData.geometry === 'string') {
            console.log('Parsing geometry string as JSON...');
            try {
                geojson = JSON.parse(boundaryData.geometry);
                console.log('✓ Parsed successfully:', geojson);
            } catch (parseError) {
                console.error('✗ Failed to parse geometry as JSON:', parseError);
                console.error('Geometry content:', boundaryData.geometry);
                alert('Error: Boundary geometry is not valid GeoJSON');
                return;
            }
        } else {
            // Already an object
            geojson = boundaryData.geometry;
            console.log('✓ Geometry is already an object');
        }
        
        // Validate GeoJSON structure
        if (!geojson.type || !geojson.coordinates) {
            console.error('✗ Invalid GeoJSON structure:', geojson);
            alert('Error: Boundary geometry missing required fields (type/coordinates)');
            return;
        }
        
        console.log('Creating Leaflet layer...');
        
        // Create boundary layer
        boundariesLayer = L.geoJSON(geojson, {
            style: {
                color: '#e74c3c',
                weight: 2,
                fillOpacity: 0,
                fillColor: 'transparent'
            },
            onEachFeature: function(feature, layer) {
                const name = communeName || boundaryData.commune_name;
                if (name) {
                    layer.bindPopup(`<b>${name}</b>`);
                    layer.bindTooltip(name, {
                        permanent: false,
                        direction: 'center',
                        className: 'boundary-tooltip'
                    });
                }
            }
        }).addTo(map);
        
        // Fit map to boundary
        console.log('Fitting map to bounds...');
        map.fitBounds(boundariesLayer.getBounds(), {
            padding: [50, 50],
            maxZoom: 14
        });
        
        console.log('✓ Boundary displayed successfully!');
    } catch (error) {
        console.error('✗ Error displaying commune boundary:', error);
        console.error('Error stack:', error.stack);
    }
}

// Create base layers object for layer control
const baseMaps = {
    "Satellite": satelliteLayer,
    "Street": streetLayer,
    "Topographic": topoLayer,
    "Light": cartoLayer,
    "Dark": darkLayer
};

// Add layer control to the map (positioned at bottom left)
L.control.layers(baseMaps, null, { position: 'bottomleft' }).addTo(map);

// Create a feature group to store all drawn items
const drawnItems = new L.FeatureGroup();
map.addLayer(drawnItems);

// Create custom marker icons for different types
const numberIcon = L.divIcon({
    className: 'number-marker',
    html: '<div style="background: #3498db; color: white; width: 20px; height: 20px; border-radius: 50%; display: flex; align-items: center; justify-content: center; font-weight: bold; font-size: 11px; border: 2px solid white; box-shadow: 0 2px 5px rgba(0,0,0,0.3);">N</div>',
    iconSize: [20, 20],
    iconAnchor: [10, 10],
    popupAnchor: [0, -10]
});

const panelIcon = L.divIcon({
    className: 'panel-marker',
    html: '<div style="background: #e74c3c; color: white; width: 20px; height: 20px; border-radius: 3px; display: flex; align-items: center; justify-content: center; font-weight: bold; font-size: 11px; border: 2px solid white; box-shadow: 0 2px 5px rgba(0,0,0,0.3);">P</div>',
    iconSize: [20, 20],
    iconAnchor: [10, 10],
    popupAnchor: [0, -10]
});

// Define polygon styles
const polygonStyles = {
    zones: {
        color: '#9b59b6',
        weight: 3,
        fillOpacity: 0.2,
        fillColor: '#9b59b6'
    },
    districts: {
        color: '#f39c12',
        weight: 3,
        fillOpacity: 0.2,
        fillColor: '#f39c12'
    },
    equipments: {
        color: '#16a085',
        weight: 3,
        fillOpacity: 0.2,
        fillColor: '#16a085'
    }
};

// Initialize the draw control
const drawControl = new L.Control.Draw({
    edit: {
        featureGroup: drawnItems,
        edit: true,
        remove: true
    },
    draw: {
        polygon: {
            allowIntersection: false,
            shapeOptions: {
                color: '#9b59b6',
                weight: 3,
                fillOpacity: 0.2
            }
        },
        polyline: {
            shapeOptions: {
                color: '#3498db',
                weight: 3
            }
        },
        rectangle: false,
        circle: false,
        circlemarker: false,
        marker: {
            icon: numberIcon  // Default icon
        }
    }
});
map.addControl(drawControl);

// Store for tracking features with their types
let allFeatures = {
    zones: [],
    districts: [],
    equipments: [],
    numbers: [],
    panels: [],
    polylines: []
};

// Variable to store the current layer waiting for label
let pendingLayer = null;
let pendingLayerType = null;

// Modal functions
function showLabelModal(baseType, callback) {
    const modal = document.getElementById('labelModal');
    const labelInput = document.getElementById('labelInput');
    const typeSelect = document.getElementById('featureTypeSelect');
    const typeContainer = document.getElementById('featureTypeContainer');
    
    labelInput.value = '';
    typeSelect.innerHTML = '<option value="">Select type...</option>';
    
    // Populate options based on base type
    if (baseType === 'polygon') {
        typeContainer.style.display = 'block';
        typeSelect.innerHTML += '<option value="zones">Zone</option>';
        typeSelect.innerHTML += '<option value="districts">District</option>';
        typeSelect.innerHTML += '<option value="equipments">Equipment</option>';
    } else if (baseType === 'marker') {
        typeContainer.style.display = 'block';
        typeSelect.innerHTML += '<option value="numbers">Number</option>';
        typeSelect.innerHTML += '<option value="panels">Panel</option>';
    } else {
        typeContainer.style.display = 'none';
        typeSelect.value = 'polylines';
    }
    
    modal.style.display = 'block';
    if (typeContainer.style.display === 'block') {
        typeSelect.focus();
    } else {
        labelInput.focus();
    }

    const saveBtn = document.getElementById('saveLabel');
    const cancelBtn = document.getElementById('cancelLabel');

    const handleSave = () => {
        const label = labelInput.value.trim();
        const featureType = typeSelect.value;
        
        if (typeContainer.style.display === 'block' && !featureType) {
            alert('Please select a feature type');
            return;
        }
        
        cleanup();
        callback(label || 'Unlabeled', featureType || 'polylines');
    };

    const handleCancel = () => {
        cleanup();
        callback(null, null);
    };

    const cleanup = () => {
        modal.style.display = 'none';
        saveBtn.removeEventListener('click', handleSave);
        cancelBtn.removeEventListener('click', handleCancel);
        document.removeEventListener('keyup', handleKeyup);
    };

    const handleKeyup = (e) => {
        if (e.key === 'Enter') handleSave();
        if (e.key === 'Escape') handleCancel();
    };

    saveBtn.addEventListener('click', handleSave);
    cancelBtn.addEventListener('click', handleCancel);
    document.addEventListener('keyup', handleKeyup);
}

// Update counts
function updateCounts() {
    document.getElementById('zonesCount').textContent = allFeatures.zones.length;
    document.getElementById('districtsCount').textContent = allFeatures.districts.length;
    document.getElementById('equipmentsCount').textContent = allFeatures.equipments.length;
    document.getElementById('numbersCount').textContent = allFeatures.numbers.length;
    document.getElementById('panelsCount').textContent = allFeatures.panels.length;
    document.getElementById('polylineCount').textContent = allFeatures.polylines.length;
}

// Create a permanent label that stays visible on the feature
function createPermanentLabel(layer, label, featureType) {
    // Determine label color based on feature type
    let labelColor = 'white';
    if (featureType === 'zones') labelColor = '#9b59b6';
    else if (featureType === 'districts') labelColor = '#f39c12';
    else if (featureType === 'equipments') labelColor = '#16a085';
    else if (featureType === 'numbers') labelColor = '#3498db';
    else if (featureType === 'panels') labelColor = '#e74c3c';
    
    // For markers, bind a permanent tooltip
    if (layer instanceof L.Marker) {
        layer.bindTooltip(label, {
            permanent: true,
            direction: 'top',
            className: 'custom-marker-label',
            offset: [0, -15]
        }).openTooltip();
    } else {
        // For polylines and polygons, bind a permanent tooltip at the center
        layer.bindTooltip(label, {
            permanent: true,
            direction: 'center',
            className: 'custom-shape-label'
        }).openTooltip();
    }
}

// Get the appropriate icon based on marker type
function getMarkerIcon(featureType) {
    if (featureType === 'panels') {
        return panelIcon;
    }
    return numberIcon; // default for 'numbers'
}

// Handle draw created event
map.on(L.Draw.Event.CREATED, function (event) {
    const layer = event.layer;
    const baseType = event.layerType;

    // Store the pending layer
    pendingLayer = layer;
    pendingLayerType = baseType;

    // Show modal to get label and specific type
    showLabelModal(baseType, async (label, featureType) => {
        if (label && featureType) {
            // For markers, update the icon based on type
            if (baseType === 'marker') {
                const icon = getMarkerIcon(featureType);
                layer.setIcon(icon);
            }
            
            // For polygons, update the style based on type
            if (baseType === 'polygon' && polygonStyles[featureType]) {
                layer.setStyle(polygonStyles[featureType]);
            }
            
            // Add layer to the map
            drawnItems.addLayer(layer);

            // Add permanent label to the layer
            createPermanentLabel(layer, label, featureType);

            // Also bind a popup for additional info
            layer.bindPopup(`<b>${label}</b><br><small>Type: ${featureType}</small>`);

            // Prepare data for database
            let featureData = {
                type: featureType,
                label: label
            };

            // Extract coordinates based on base type
            if (baseType === 'marker') {
                const latlng = layer.getLatLng();
                featureData.lat = latlng.lat;
                featureData.lng = latlng.lng;
                allFeatures[featureType].push({ layer, data: featureData });
            } else if (baseType === 'polyline') {
                const latlngs = layer.getLatLngs();
                featureData.coordinates = latlngs.map(ll => ({ lat: ll.lat, lng: ll.lng }));
                allFeatures.polylines.push({ layer, data: featureData });
            } else if (baseType === 'polygon') {
                const latlngs = layer.getLatLngs()[0];
                featureData.coordinates = latlngs.map(ll => ({ lat: ll.lat, lng: ll.lng }));
                allFeatures[featureType].push({ layer, data: featureData });
            }

            // Save to database
            await saveToDatabase(featureData);
            updateCounts();
        } else {
            // User cancelled, don't add the layer
            console.log('Drawing cancelled');
        }

        pendingLayer = null;
        pendingLayerType = null;
    });
});

// Database functions
async function saveToDatabase(featureData) {
    try {
        const response = await fetch('/api/save', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
            },
            body: JSON.stringify(featureData)
        });
        
        if (!response.ok) {
            console.error('Failed to save to database');
        } else {
            console.log('Feature saved successfully:', featureData.type);
        }
    } catch (error) {
        console.error('Error saving to database:', error);
    }
}

async function loadFromDatabase() {
    try {
        const response = await fetch('/api/load');
        const features = await response.json();
        
        // Clear existing features
        drawnItems.clearLayers();
        allFeatures = {
            zones: [],
            districts: [],
            equipments: [],
            numbers: [],
            panels: [],
            polylines: []
        };
        
        // Load features
        features.forEach(feature => {
            const data = JSON.parse(feature.data);
            let layer;
            
            // Handle marker types (numbers, panels)
            if (data.type === 'numbers' || data.type === 'panels') {
                const icon = getMarkerIcon(data.type);
                layer = L.marker([data.lat, data.lng], { icon: icon });
                drawnItems.addLayer(layer);
                createPermanentLabel(layer, data.label, data.type);
                layer.bindPopup(`<b>${data.label}</b><br><small>Type: ${data.type}</small>`);
                allFeatures[data.type].push({ layer, data });
            } 
            // Handle polylines
            else if (data.type === 'polylines') {
                layer = L.polyline(
                    data.coordinates.map(c => [c.lat, c.lng]), 
                    { color: '#3498db', weight: 3 }
                );
                drawnItems.addLayer(layer);
                createPermanentLabel(layer, data.label, data.type);
                layer.bindPopup(`<b>${data.label}</b><br><small>Type: ${data.type}</small>`);
                allFeatures.polylines.push({ layer, data });
            } 
            // Handle polygon types (zones, districts, equipments)
            else if (data.type === 'zones' || data.type === 'districts' || data.type === 'equipments') {
                const style = polygonStyles[data.type] || {
                    color: '#e74c3c',
                    weight: 3,
                    fillOpacity: 0.2
                };
                layer = L.polygon(
                    data.coordinates.map(c => [c.lat, c.lng]), 
                    style
                );
                drawnItems.addLayer(layer);
                createPermanentLabel(layer, data.label, data.type);
                layer.bindPopup(`<b>${data.label}</b><br><small>Type: ${data.type}</small>`);
                allFeatures[data.type].push({ layer, data });
            }
        });
        
        updateCounts();
        console.log('Data loaded from database');
    } catch (error) {
        console.error('Error loading from database:', error);
    }
}

// Initialize counts
updateCounts();

// Auto-navigate to user's commune
async function navigateToUserCommune() {
    try {
        // Get current user information
        const response = await fetch('/api/current_user');
        if (!response.ok) {
            console.error('Failed to get user information');
            return;
        }
        
        const user = await response.json();
        console.log('Current user:', user);
        
        if (user.commune && user.commune.id) {
            // Fetch and display the boundary for user's commune
            await displayCommuneBoundary(user.commune.id, user.commune.name_fr);
            
            // Center map on commune coordinates if available
            if (user.commune.latitude && user.commune.longitude) {
                const lat = parseFloat(user.commune.latitude);
                const lng = parseFloat(user.commune.longitude);
                if (!isNaN(lat) && !isNaN(lng)) {
                    map.setView([lat, lng], 13);
                    console.log(`Centered map on commune coordinates: [${lat}, ${lng}]`);
                }
            }
            
            console.log('Navigated to user commune:', user.commune.name_fr);
        }
    } catch (error) {
        console.error('Error navigating to user commune:', error);
    }
}

// Auto-load data on page load
window.addEventListener('DOMContentLoaded', async () => {
    // Navigate to user's commune (will fetch boundary from database)
    await navigateToUserCommune();
    
    // Then load saved features
    await loadFromDatabase();
});

// Add custom CSS for tooltips
const style = document.createElement('style');
style.textContent = `
    .custom-marker-label {
        background: transparent !important;
        border: none !important;
        box-shadow: none !important;
        font-weight: 700 !important;
        font-size: 12px !important;
        color: white !important;
        text-shadow: 
            -1px -1px 0 #000,
            1px -1px 0 #000,
            -1px 1px 0 #000,
            1px 1px 0 #000,
            -2px 0 0 #000,
            2px 0 0 #000,
            0 -2px 0 #000,
            0 2px 0 #000 !important;
        padding: 4px 8px !important;
    }
    
    .custom-marker-label::before {
        display: none !important;
    }
    
    .custom-shape-label {
        background: transparent !important;
        border: none !important;
        box-shadow: none !important;
        font-weight: 700 !important;
        font-size: 13px !important;
        color: white !important;
        text-shadow: 
            -1px -1px 0 #000,
            1px -1px 0 #000,
            -1px 1px 0 #000,
            1px 1px 0 #000,
            -2px 0 0 #000,
            2px 0 0 #000,
            0 -2px 0 #000,
            0 2px 0 #000 !important;
        padding: 4px 8px !important;
    }
    
    .boundary-tooltip {
        background: rgba(231, 76, 60, 0.9) !important;
        border: 2px solid white !important;
        border-radius: 4px !important;
        color: white !important;
        font-weight: 700 !important;
        font-size: 12px !important;
        padding: 5px 10px !important;
        box-shadow: 0 2px 5px rgba(0,0,0,0.3) !important;
    }
`;
document.head.appendChild(style);

// Profile menu functionality
const profileButton = document.getElementById('profileButton');
const profileDropdown = document.getElementById('profileDropdown');
const dropdownArrow = document.getElementById('dropdownArrow');
const settingsItem = document.getElementById('settingsItem');
const logoutItem = document.getElementById('logoutItem');

// Load and display user information
async function loadUserProfile() {
    try {
        const response = await fetch('/api/current_user');
        if (response.ok) {
            const user = await response.json();
            
            // Update profile display
            document.getElementById('profileUsername').textContent = user.username;
            document.getElementById('profileName').textContent = user.name;
            
            // Set profile icon to first letter of username
            document.getElementById('profileIcon').textContent = user.username.charAt(0).toUpperCase();
            
            console.log('User profile loaded:', user.username);
        }
    } catch (error) {
        console.error('Error loading user profile:', error);
    }
}

// Toggle dropdown menu
profileButton.addEventListener('click', (e) => {
    e.stopPropagation();
    profileDropdown.classList.toggle('show');
    dropdownArrow.classList.toggle('open');
});

// Close dropdown when clicking outside
document.addEventListener('click', (e) => {
    if (!profileButton.contains(e.target) && !profileDropdown.contains(e.target)) {
        profileDropdown.classList.remove('show');
        dropdownArrow.classList.remove('open');
    }
});

// Settings action
settingsItem.addEventListener('click', () => {
    alert('Settings functionality coming soon!');
    profileDropdown.classList.remove('show');
    dropdownArrow.classList.remove('open');
});

// Logout action
logoutItem.addEventListener('click', async () => {
    try {
        const response = await fetch('/api/logout', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
            }
        });
        
        if (response.ok) {
            // Redirect to login page
            window.location.href = '/login';
        } else {
            alert('Logout failed. Please try again.');
        }
    } catch (error) {
        console.error('Error logging out:', error);
        alert('Logout failed. Please try again.');
    }
});

// Load user profile on page load
loadUserProfile();

console.log('Map initialized with custom feature types');
