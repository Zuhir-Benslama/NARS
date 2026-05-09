import java.util.Properties

plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.android)
    alias(libs.plugins.kotlin.serialization)
    alias(libs.plugins.kotlin.compose)
}

// Load local.properties if it exists
val localProperties = Properties()
val localPropertiesFile = rootProject.file("local.properties")
if (localPropertiesFile.exists()) {
    localProperties.load(localPropertiesFile.inputStream())
}

android {
    namespace = "com.nars.maplibre"
    compileSdk = 35

    defaultConfig {
        applicationId = "com.nars.maplibre"
        minSdk = 24
        targetSdk = 34
        versionCode = 1
        versionName = "1.0.0"

        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
        vectorDrawables {
            useSupportLibrary = true
        }

        // Build config fields from local.properties
        val apiUrl = localProperties.getProperty("NARS_API_BASE_URL", "https://api.nars.example.com")
        buildConfigField("String", "API_BASE_URL", "\"$apiUrl\"")
        buildConfigField("Boolean", "ENABLE_ANALYTICS", localProperties.getProperty("ENABLE_ANALYTICS", "false").toBoolean().toString())
        buildConfigField("Boolean", "ENABLE_CRASHLYTICS", localProperties.getProperty("ENABLE_CRASHLYTICS", "false").toBoolean().toString())
        buildConfigField("Boolean", "MTLS_ENABLED", localProperties.getProperty("MTLS_ENABLED", "false").toBoolean().toString())
        buildConfigField("String", "CA_CERT_ASSET", "\"${localProperties.getProperty("CA_CERT_ASSET", "nars-ca.crt")}\"")
        buildConfigField("String", "CLIENT_P12_ASSET", "\"${localProperties.getProperty("CLIENT_P12_ASSET", "nars-client.p12")}\"")
        // SECURITY: P12 password is NOT compiled into the APK.
        // Provide it at runtime via SecurePreferences or a runtime prompt.
        // buildConfigField("String", "CLIENT_P12_PASSWORD", "\"${localProperties.getProperty("CLIENT_P12_PASSWORD", "")}\"")
        buildConfigField("String", "CERTIFICATE_PINS", "\"${localProperties.getProperty("CERTIFICATE_PINS", "")}\"")
    }

    buildTypes {
        release {
            isMinifyEnabled = true
            isDebuggable = false
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro"
            )
        }
        debug {
            isMinifyEnabled = false
            isDebuggable = true
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    buildFeatures {
        compose = true
        buildConfig = true
    }

    packaging {
        resources {
            excludes += "/META-INF/{AL2.0,LGPL2.1}"
        }
    }

    testOptions {
        unitTests.isReturnDefaultValues = true
    }
}

tasks.withType<org.jetbrains.kotlin.gradle.tasks.KotlinCompile> {
    compilerOptions {
        jvmTarget = org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17
    }
}

dependencies {
    // MapLibre Android SDK 11.x
    implementation(libs.maplibre.android.sdk)

    // MapLibre Geoman Android (local module)
    implementation(project(":geoman"))

    // Kotlin
    implementation(libs.kotlin.stdlib)
    implementation(libs.kotlinx.coroutines.android)
    implementation(libs.kotlinx.serialization.json)

    // AndroidX Core
    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.appcompat)
    implementation(libs.androidx.activity.compose)
    implementation(libs.androidx.lifecycle.runtime.compose)
    implementation(libs.androidx.lifecycle.viewmodel.compose)

    // Compose BOM
    implementation(platform(libs.compose.bom))
    implementation(libs.compose.ui)
    implementation(libs.compose.ui.graphics)
    implementation(libs.compose.ui.tooling.preview)
    implementation(libs.compose.material3)
    implementation(libs.compose.material.icons.extended)

    // Navigation
    implementation(libs.androidx.navigation.compose)

    // DataStore for preferences
    implementation(libs.androidx.datastore.preferences)

    // Security - EncryptedSharedPreferences
    implementation(libs.androidx.security.crypto.ktx)

    // Coil for image loading
    implementation(libs.coil.compose)

    // Koin DI
    implementation(libs.koin.core)
    implementation(libs.koin.android)
    implementation(libs.koin.androidx.compose)

    // Ktor HTTP Client
    implementation(libs.ktor.client.okhttp)
    implementation(libs.ktor.client.content.negotiation)
    implementation(libs.ktor.serialization.kotlinx.json)
    implementation(libs.ktor.client.logging)

    // Testing
    testImplementation(libs.junit)
    testImplementation(libs.kotlinx.coroutines.test)
    testImplementation(libs.mockk)
    testImplementation(libs.ktor.client.mock)
    androidTestImplementation(libs.androidx.test.ext.junit)
    androidTestImplementation(libs.androidx.espresso.core)
    androidTestImplementation(platform(libs.compose.bom))
    androidTestImplementation(libs.compose.ui.test.junit4)
    debugImplementation(libs.compose.ui.tooling)
    debugImplementation(libs.compose.ui.test.manifest)
}
