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
project(":geoman").projectDir = file("../maplibre-geoman-android/app")
