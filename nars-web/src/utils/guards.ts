export function isPoint(g: GeoJSON.Geometry): g is GeoJSON.Point {
  return g.type === "Point"
}

export function isLineString(g: GeoJSON.Geometry): g is GeoJSON.LineString {
  return g.type === "LineString"
}

export function isPolygon(g: GeoJSON.Geometry): g is GeoJSON.Polygon {
  return g.type === "Polygon"
}

export function isMultiPolygon(g: GeoJSON.Geometry): g is GeoJSON.MultiPolygon {
  return g.type === "MultiPolygon"
}

export function isMultiLineString(g: GeoJSON.Geometry): g is GeoJSON.MultiLineString {
  return g.type === "MultiLineString"
}

export function isMultiPoint(g: GeoJSON.Geometry): g is GeoJSON.MultiPoint {
  return g.type === "MultiPoint"
}
