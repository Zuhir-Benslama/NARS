package com.nars.maplibre.di

import com.nars.maplibre.AppPreferences
import com.nars.maplibre.BuildConfig
import com.nars.maplibre.MapViewModel
import com.nars.maplibre.SettingsViewModel
import com.nars.maplibre.data.api.ApiService
import com.nars.maplibre.data.api.SessionManager
import com.nars.maplibre.data.store.FeatureStore
import com.nars.maplibre.domain.ComputeRoadDirectionsUseCase
import com.nars.maplibre.domain.GenerateNamingPanelsUseCase
import com.nars.maplibre.domain.SetHouseNumbersUseCase
import com.nars.maplibre.utils.TlsUtils
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
import java.util.concurrent.TimeUnit

val appModule = module {
    single {
        val tlsConfig = TlsUtils.getTlsConfig(androidContext())
        HttpClient(OkHttp) {
            engine {
                config {
                    connectTimeout(10, TimeUnit.SECONDS)
                    readTimeout(15, TimeUnit.SECONDS)
                    writeTimeout(15, TimeUnit.SECONDS)
                    retryOnConnectionFailure(true)
                    tlsConfig?.let { config ->
                        sslSocketFactory(config.socketFactory, config.trustManager)
                    }
                }
            }
            install(ContentNegotiation) {
                json(Json {
                    ignoreUnknownKeys = true
                    isLenient = true
                })
            }
            install(Logging) {
                level = LogLevel.HEADERS
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

    single { ComputeRoadDirectionsUseCase(get()) }
    single { GenerateNamingPanelsUseCase(get(), get()) }
    single { SetHouseNumbersUseCase(get()) }

    viewModel { MapViewModel(get(), get(), get(), get(), get(), get()) }
    viewModel { SettingsViewModel(get()) }
}
