import java.util.Properties

plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.android)
    alias(libs.plugins.kotlin.compose)
    alias(libs.plugins.ksp)
    alias(libs.plugins.hilt)
}

// Read developer-specific config from local.properties (never committed to VCS)
val localProps = Properties().also { props ->
    val f = rootProject.file("local.properties")
    if (f.exists()) props.load(f.inputStream())
}

android {
    namespace   = "com.nars.narstreet"
    compileSdk  = 35
    ndkVersion  = "27.3.13750724"

    defaultConfig {
        applicationId   = "com.nars.narstreet"
        minSdk          = 26
        targetSdk       = 35
        versionCode     = 1
        versionName     = "1.0.0-alpha"
    }

    buildTypes {
        debug {
            // Reads DEV_API_URL from local.properties.
            // Falls back to the Android emulator loopback (10.0.2.2) so the
            // project builds out-of-the-box without any local.properties entry.
            val devUrl = localProps.getProperty("DEV_API_URL", "http://10.0.2.2:5000")
            buildConfigField("String", "API_BASE_URL", "\"$devUrl\"")
        }
        release {
            // Reads PROD_API_URL from local.properties (or CI secret).
            // Fails the build explicitly if missing — better than shipping a
            // placeholder URL that silently fails at runtime.
            val prodUrl = localProps.getProperty("PROD_API_URL")
                ?: error("PROD_API_URL must be set in local.properties for a release build")
            buildConfigField("String", "API_BASE_URL", "\"$prodUrl\"")
            isMinifyEnabled = true
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    kotlin {
        compilerOptions {
            jvmTarget.set(org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17)
        }
    }

    buildFeatures {
        compose     = true
        buildConfig = true
    }
}

dependencies {
    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.lifecycle.runtime.ktx)
    implementation(libs.androidx.lifecycle.viewmodel.ktx)
    implementation(libs.androidx.lifecycle.viewmodel.compose)
    implementation(libs.androidx.lifecycle.runtime.compose)
    implementation(libs.androidx.activity.compose)
    implementation(libs.splashscreen)

    // Compose
    implementation(platform(libs.androidx.compose.bom))
    implementation(libs.androidx.ui)
    implementation(libs.androidx.ui.graphics)
    implementation(libs.androidx.ui.tooling.preview)
    implementation(libs.androidx.material3)
    implementation(libs.androidx.material.icons.extended)
    implementation(libs.androidx.navigation.compose)

    // Hilt DI
    implementation(libs.hilt.android)
    ksp(libs.hilt.compiler)
    implementation(libs.hilt.navigation.compose)
    implementation(libs.hilt.work)
    ksp(libs.hilt.work.compiler)

    // Room
    implementation(libs.room.runtime)
    implementation(libs.room.ktx)
    ksp(libs.room.compiler)

    // DataStore
    implementation(libs.datastore.preferences)

    // Networking
    implementation(libs.retrofit)
    implementation(libs.retrofit.moshi)
    implementation(libs.okhttp.logging)
    ksp(libs.moshi.codegen)

    // Coroutines
    implementation(libs.coroutines.android)

    // WorkManager
    implementation(libs.work.runtime.ktx)


    // Map (MapLibre native SDK — LatLng type + MapView.kt legacy composables)
    implementation(libs.maplibre.android)

    // GPS
    implementation(libs.play.services.location)

    debugImplementation(libs.androidx.ui.tooling.preview)
}
// Tell Room KSP where to write the exported schema JSON files.
// Commit the generated schemas/ directory to VCS for migration audits.
ksp {
    arg("room.schemaLocation", "$projectDir/schemas")
    arg("room.incremental", "true")
}

tasks.withType<JavaCompile>().configureEach {
    options.compilerArgs.add("-Xlint:-processing")
}