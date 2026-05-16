pluginManagement {
    repositories {
        google()
        mavenCentral()
        gradlePluginPortal()
    }
}

dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories {
        google()
        mavenCentral()
        maven { url = uri("https://oss.sonatype.org/content/repositories/snapshots/") }
        maven { url = uri("https://maplibre.org/maven") }
    }
}

rootProject.name = "nars-android-maplibre"
include(":app")
include(":geoman")

// Include maplibre-geoman-android app module as :geoman
// Use local.properties geoman.dir if set, otherwise default to sibling path
val geomanDir = settings.extensions.extraProperties.properties["geoman.dir"]?.toString()
    ?: providers.gradleProperty("geoman.dir").orNull
    ?: System.getProperty("geoman.dir")
    ?: System.getenv("GEOMAN_DIR")
    ?: "../maplibre-geoman-android/app"
project(":geoman").projectDir = file(geomanDir)
