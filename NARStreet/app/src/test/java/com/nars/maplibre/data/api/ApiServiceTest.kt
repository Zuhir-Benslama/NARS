package com.nars.maplibre.data.api

import com.nars.maplibre.AppPreferences
import com.nars.maplibre.data.model.NarsFeature
import com.nars.maplibre.data.model.NarsFeatureType
import com.nars.maplibre.data.model.FeatureProperties
import com.nars.maplibre.data.model.PointGeometry
import com.nars.maplibre.data.model.LoginRequest
import io.ktor.client.HttpClient
import io.ktor.client.engine.mock.MockEngine
import io.ktor.client.engine.mock.respond
import io.ktor.client.plugins.contentnegotiation.ContentNegotiation
import io.ktor.http.ContentType
import io.ktor.http.HttpHeaders
import io.ktor.http.HttpStatusCode
import io.ktor.http.contentType
import io.ktor.serialization.kotlinx.json.json
import io.mockk.every
import io.mockk.mockk
import kotlinx.coroutines.test.runTest
import kotlinx.serialization.json.Json
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

class ApiServiceTest {

    private val appPreferences: AppPreferences = mockk()
    private lateinit var apiService: ApiService
    private lateinit var engine: MockEngine

    @Before
    fun setUp() {
        engine = MockEngine { _ ->
            respond(
                content = """{"success": true, "user": {"id": "1", "username": "test", "name": "Test"}}""",
                status = HttpStatusCode.OK,
                headers = io.ktor.http.headersOf("Set-Cookie", "access_token=test123")
            )
        }
        val client = HttpClient(engine) {
            install(ContentNegotiation) {
                json(Json {
                    ignoreUnknownKeys = true
                    isLenient = true
                })
            }
        }
        every { appPreferences.authToken } returns null
        every { appPreferences.isLoggedIn } returns false
        apiService = ApiService(client, appPreferences)
    }

    @Test
    fun `login parses response correctly`() = runTest {
        val result = apiService.login("testuser", "password")

        assertTrue(result.isSuccess)
        val loginResponse = result.getOrNull()
        assertNotNull(loginResponse)
        assertEquals("Test", loginResponse?.user?.name)
        assertEquals("test", loginResponse?.user?.username)
    }

    @Test
    fun `login handles failure response`() = runTest {
        engine = MockEngine { _ ->
            respond(
                content = """{"success": false, "message": "Invalid credentials"}""",
                status = HttpStatusCode.Unauthorized
            )
        }
        val client = HttpClient(engine) {
            install(ContentNegotiation) {
                json(Json { ignoreUnknownKeys = true; isLenient = true })
            }
        }
        apiService = ApiService(client, appPreferences)

        val result = apiService.login("testuser", "wrong")

        assertTrue(result.isFailure)
    }

    @Test
    fun `login extracts cookie from response headers`() = runTest {
        val result = apiService.login("testuser", "password")

        assertTrue(result.isSuccess)
        assertEquals("test123", apiService.getCookie())
    }

    @Test
    fun `loadFeatures returns empty list for blank body`() = runTest {
        engine = MockEngine { _ ->
            respond(
                content = "",
                status = HttpStatusCode.OK
            )
        }
        val client = HttpClient(engine) {
            install(ContentNegotiation) {
                json(Json { ignoreUnknownKeys = true; isLenient = true })
            }
        }
        apiService = ApiService(client, appPreferences)

        val result = apiService.loadFeatures()

        assertTrue(result.isSuccess)
        assertTrue(result.getOrNull()?.isEmpty() == true)
    }

    @Test
    fun `setAuthToken and getAuthToken round trip`() {
        apiService.setAuthToken("token123")
        assertEquals("token123", apiService.getAuthToken())
    }

    @Test
    fun `setCookie and getCookie round trip`() {
        apiService.setCookie("cookie123")
        assertEquals("cookie123", apiService.getCookie())
    }
}
