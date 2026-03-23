package com.nars.narstreet.ui.components

import android.annotation.SuppressLint
import android.content.res.AssetManager
import android.os.Handler
import android.os.Looper
import android.webkit.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.viewinterop.AndroidView
import org.maplibre.android.geometry.LatLng
import org.json.JSONArray
import org.json.JSONObject

private val mainHandler = Handler(Looper.getMainLooper())
private fun onMain(block: () -> Unit) = mainHandler.post(block)

// ── JavaScript bridge (JS → Kotlin) ──────────────────────────────────────────

class MapBridge(
    var onMapReady: () -> Unit                                  = {},
    var onFeatureClick: (type: String, dbId: Long) -> Unit      = { _, _ -> },
    var onFeatureLongClick: (type: String, dbId: Long) -> Unit  = { _, _ -> },
    var onGeometryChanged: (featureId: String, pointsJson: String) -> Unit = { _, _ -> },
) {
    // All @JavascriptInterface methods are called on a background thread.
    // Post to main thread before touching Compose state.

    @JavascriptInterface
    fun onMapReady() = onMain { onMapReady.invoke() }

    @JavascriptInterface
    fun onFeatureClick(type: String, dbId: String) =
        onMain { onFeatureClick.invoke(type, dbId.toLongOrNull() ?: return@onMain) }

    @JavascriptInterface
    fun onFeatureLongClick(type: String, dbId: String) =
        onMain { onFeatureLongClick.invoke(type, dbId.toLongOrNull() ?: return@onMain) }

    @JavascriptInterface
    fun onGeometryChanged(featureId: String, pointsJson: String) =
        onMain { onGeometryChanged.invoke(featureId, pointsJson) }
}

// ── Composable ────────────────────────────────────────────────────────────────

@SuppressLint("SetJavaScriptEnabled")
@Composable
fun NarsMapView(
    modifier: Modifier = Modifier,
    onBridge: (MapBridge, WebView) -> Unit = { _, _ -> },
) {
    AndroidView(
        modifier = modifier,
        factory  = { ctx ->
            WebView(ctx).apply {
                settings.apply {
                    javaScriptEnabled = true
                    domStorageEnabled = true
                }
                webChromeClient = WebChromeClient()

                // Intercept requests to serve assets from the app's asset folder.
                // Using "https://appassets.local/" as the base URL gives a secure
                // HTTPS origin without any deprecated file-access flags.
                webViewClient = object : WebViewClient() {
                    override fun shouldInterceptRequest(
                        view: WebView,
                        request: WebResourceRequest,
                    ): WebResourceResponse? {
                        val url = request.url
                        if (url.host == "appassets.local") {
                            val assetPath = url.path?.trimStart('/') ?: return null
                            return try {
                                val assets: AssetManager = ctx.assets
                                val stream = assets.open(assetPath)
                                val mime = when {
                                    assetPath.endsWith(".html") -> "text/html"
                                    assetPath.endsWith(".js")   -> "application/javascript"
                                    assetPath.endsWith(".css")  -> "text/css"
                                    else                        -> "application/octet-stream"
                                }
                                WebResourceResponse(mime, "UTF-8", stream)
                            } catch (_: Exception) { null }
                        }
                        return null
                    }
                }

                val bridge = MapBridge()
                addJavascriptInterface(bridge, "NarsAndroid")
                loadUrl("https://appassets.local/map.html")
                onBridge(bridge, this)
            }
        },
    )
}

// ── WebView extension — type-safe JS calls ────────────────────────────────────

fun WebView.mapCall(js: String) =
    post { evaluateJavascript("window.nars.$js", null) }

fun WebView.flyTo(lat: Double, lng: Double, zoom: Double = 14.0) =
    mapCall("flyTo($lat,$lng,$zoom)")

fun WebView.setContext(ctx: CommuneMapContext?) {
    if (ctx == null) return
    val boundary = ctx.boundaryGeoJson?.let { "'${escapeJson(it)}'" } ?: "null"
    val main     = ctx.mainAreaGeoJson?.let { "'${escapeJson(it)}'" } ?: "null"
    val sec      = ctx.secondaryAreasGeoJson.joinToString(",") { "'${escapeJson(it)}'" }
    mapCall("setContext($boundary,$main,[$sec])")
}

fun WebView.setAreas(polygons: List<List<LatLng>>, labels: List<String> = emptyList()) =
    mapCall("setAreas('${polygonsToFcJson(polygons, labels)}')")

fun WebView.setDistricts(polygons: List<List<LatLng>>, labels: List<String> = emptyList()) =
    mapCall("setDistricts('${polygonsToFcJson(polygons, labels)}')")

fun WebView.setRoads(
    polylines: List<List<LatLng>>,
    dbIds: List<Long>        = emptyList(),
    labels: List<String>     = emptyList(),
    highlight: List<LatLng>? = null,
) {
    val fc = linesToFcJson(polylines, dbIds, labels)
    val hl = highlight?.let { "'${lineToFcJson(it)}'" } ?: "null"
    mapCall("setRoads('$fc',$hl)")
}

fun WebView.setEntrances(points: List<LatLng>, dbIds: List<Long> = emptyList(), labels: List<String> = emptyList()) =
    mapCall("setEntrances('${pointsToFcJson(points, dbIds, labels)}')")

fun WebView.setGps(lat: Double, lng: Double) =
    mapCall("setGps($lat,$lng)")

fun WebView.setCityCenter(lat: Double, lng: Double) =
    mapCall("setCityCenter($lat,$lng)")

fun WebView.setBuildings(points: List<LatLng>, dbIds: List<Long> = emptyList(), labels: List<String> = emptyList()) =
    mapCall("setBuildings('${pointsToFcJson(points, dbIds, labels)}')")

fun WebView.setSpaces(polygons: List<List<LatLng>>, labels: List<String> = emptyList()) =
    mapCall("setSpaces('${polygonsToFcJson(polygons, labels)}')")

fun WebView.setPanels(points: List<LatLng>, dbIds: List<Long> = emptyList(), labels: List<String> = emptyList()) =
    mapCall("setPanels('${pointsToFcJson(points, dbIds, labels)}')")

fun WebView.startEditPolygon(featureId: String, coords: List<LatLng>, color: String) =
    mapCall("startEditPolygon('$featureId','${escapeJson(coordsToPolygonFeatureJson(coords))}','$color')")

fun WebView.startEditPolyline(featureId: String, coords: List<LatLng>, color: String) =
    mapCall("startEditPolyline('$featureId','${escapeJson(coordsToLineFeatureJson(coords))}','$color')")

fun WebView.startEditPoint(featureId: String, lat: Double, lng: Double, color: String) {
    val feat = """{"type":"Feature","geometry":{"type":"Point","coordinates":[$lng,$lat]},"properties":{}}"""
    mapCall("startEditPoint('$featureId','${escapeJson(feat)}','$color')")
}

fun WebView.stopEdit() = mapCall("stopEdit()")

// ── GeoJSON builders ──────────────────────────────────────────────────────────

private fun escapeJson(s: String): String =
    s.replace("\\", "\\\\").replace("'", "\\'").replace("\n", "").replace("\r", "")

private fun LatLng.toLngLatArray() = "[${longitude},${latitude}]"

private fun polygonsToFcJson(polys: List<List<LatLng>>, labels: List<String> = emptyList()): String {
    val feats = polys.filter { it.size >= 3 }.mapIndexed { i, pts ->
        val ring  = pts.map { it.toLngLatArray() } + pts.first().toLngLatArray()
        val label = labels.getOrElse(i) { "" }
        val props = if (label.isNotBlank()) """{"label":"$label"}""" else "{}"
        """{"type":"Feature","geometry":{"type":"Polygon","coordinates":[[${ring.joinToString(",")}]]},"properties":$props}"""
    }
    return """{"type":"FeatureCollection","features":[${feats.joinToString(",")}]}"""
}

private fun linesToFcJson(lines: List<List<LatLng>>, ids: List<Long> = emptyList(), labels: List<String> = emptyList()): String {
    val feats = lines.filter { it.size >= 2 }.mapIndexed { i, pts ->
        val coords = pts.joinToString(",") { it.toLngLatArray() }
        val dbId   = ids.getOrElse(i) { 0L }
        val label  = labels.getOrElse(i) { "" }
        """{"type":"Feature","geometry":{"type":"LineString","coordinates":[$coords]},"properties":{"dbId":$dbId,"label":"$label"}}"""
    }
    return """{"type":"FeatureCollection","features":[${feats.joinToString(",")}]}"""
}

private fun lineToFcJson(pts: List<LatLng>): String {
    val coords = pts.joinToString(",") { it.toLngLatArray() }
    return """{"type":"FeatureCollection","features":[{"type":"Feature","geometry":{"type":"LineString","coordinates":[$coords]},"properties":{}}]}"""
}

private fun pointsToFcJson(pts: List<LatLng>, ids: List<Long> = emptyList(), labels: List<String> = emptyList()): String {
    val feats = pts.mapIndexed { i, pt ->
        val dbId  = ids.getOrElse(i) { 0L }
        val label = labels.getOrElse(i) { "" }
        """{"type":"Feature","geometry":{"type":"Point","coordinates":${pt.toLngLatArray()}},"properties":{"dbId":$dbId,"label":"$label"}}"""
    }
    return """{"type":"FeatureCollection","features":[${feats.joinToString(",")}]}"""
}

private fun coordsToPolygonFeatureJson(coords: List<LatLng>): String {
    if (coords.size < 3) return "{}"
    val ring = coords.map { it.toLngLatArray() } + coords.first().toLngLatArray()
    return """{"type":"Feature","geometry":{"type":"Polygon","coordinates":[[${ring.joinToString(",")}]]},"properties":{}}"""
}

private fun coordsToLineFeatureJson(coords: List<LatLng>): String {
    val pts = coords.joinToString(",") { it.toLngLatArray() }
    return """{"type":"Feature","geometry":{"type":"LineString","coordinates":[$pts]},"properties":{}}"""
}

// ── Parse geometry changed callback from JS ───────────────────────────────────

fun parseGeometryPoints(pointsJson: String): List<LatLng> = try {
    val arr = JSONArray(pointsJson)
    (0 until arr.length()).map { i ->
        val obj = arr.getJSONObject(i)
        LatLng(obj.getDouble("lat"), obj.getDouble("lng"))
    }
} catch (_: Exception) { emptyList() }
