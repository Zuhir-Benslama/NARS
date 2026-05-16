package com.nars.maplibre.di

import com.nars.maplibre.AppPreferences
import com.nars.maplibre.BuildConfig
import com.nars.maplibre.MapViewModel
import com.nars.maplibre.SettingsViewModel
import com.nars.maplibre.data.api.ApiService
import com.nars.maplibre.data.api.SessionManager
import com.nars.maplibre.data.store.FeatureStore
import io.ktor.client.HttpClient
import io.ktor.client.engine.okhttp.OkHttp
import io.ktor.client.plugins.HttpTimeout
import io.ktor.client.plugins.contentnegotiation.ContentNegotiation
import io.ktor.client.plugins.defaultRequest
import io.ktor.client.plugins.logging.LogLevel
import io.ktor.client.plugins.logging.Logging
import io.ktor.http.ContentType
import io.ktor.http.contentType
import io.ktor.serialization.kotlinx.json.json
import kotlinx.serialization.json.Json
import org.koin.android.ext.koin.androidContext
import org.koin.core.module.dsl.viewModel
import org.koin.dsl.module

val appModule = module {
    single {
        HttpClient(OkHttp) {
            engine {
                config {
                    retryOnConnectionFailure(true)
                }
            }
            install(ContentNegotiation) {
                json(Json {
                    ignoreUnknownKeys = true
                    isLenient = true
                })
            }
            install(Logging) {
                level = LogLevel.NONE
            }
            install(HttpTimeout) {
                requestTimeoutMillis = 15000
                connectTimeoutMillis = 10000
                socketTimeoutMillis = 15000
            }
            defaultRequest {
                val baseUrl = BuildConfig.API_BASE_URL.trimEnd('/')
                url(baseUrl)
                contentType(ContentType.Application.Json)
            }
        }
    }

    single { AppPreferences(androidContext()) }

    single { FeatureStore() }

    single { ApiService(get(), get()) }

    single { SessionManager(get(), get()) }

    viewModel { MapViewModel(get(), get(), get()) }
    viewModel { SettingsViewModel(get()) }
}
