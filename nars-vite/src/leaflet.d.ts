// Leaflet is loaded via CDN <script> tag in index.html and declared as an
// external in vite.config.ts (globals: { leaflet: 'L' }).
// Importing 'leaflet' in TS files resolves to @types/leaflet for type info,
// and maps to the global window.L at runtime.
/// <reference types="leaflet" />
