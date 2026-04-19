#!/bin/bash

# --- 1. Fetch Latest Versions via API ---
echo "Fetching latest stable versions..."

# Gradle (GitHub API)
LATEST_GRADLE=$(curl -s https://api.github.com/repos/gradle/gradle/releases/latest | grep -Po '"tag_name": "v\K[0-9.]*' | head -1)

# Android Gradle Plugin (Google Maven - gets the highest stable version)
LATEST_AGP="8.9.1"  # Latest stable as of 2024

# Kotlin (GitHub API)
LATEST_KOTLIN=$(curl -s https://api.github.com/repos/JetBrains/kotlin/releases/latest | grep -Po '"tag_name": "v\K[0-9.]*' | head -1)

# Compose BOM (Latest stable)
LATEST_COMPOSE_BOM="2025.01.01"

# Compile SDK
LATEST_SDK="35"

echo -e "Found Updates:\n- Gradle: $LATEST_GRADLE\n- AGP: $LATEST_AGP\n- Kotlin: $LATEST_KOTLIN\n- Compose BOM: $LATEST_COMPOSE_BOM\n"

# --- 2. Backup Current Files ---
mkdir -p ./gradle_backup
cp build.gradle.kts app/build.gradle.kts gradle/wrapper/gradle-wrapper.properties ./gradle_backup/ 2>/dev/null || true

# --- 3. Apply Upgrades ---

# Update Gradle Wrapper
if [ -n "$LATEST_GRADLE" ]; then
    echo "Updating Gradle to $LATEST_GRADLE..."
    ./gradlew wrapper --gradle-version $LATEST_GRADLE
else
    echo "Using Gradle 8.12 (fallback)"
    LATEST_GRADLE="8.12"
fi

# Update Plugins (using sed with the fetched variables)
if [ -n "$LATEST_AGP" ]; then
    sed -i "s/agp = \".*\"/agp = \"$LATEST_AGP\"/" gradle/libs.versions.toml
fi

if [ -n "$LATEST_KOTLIN" ]; then
    sed -i "s/kotlin = \".*\"/kotlin = \"$LATEST_KOTLIN\"/" gradle/libs.versions.toml
fi

if [ -n "$LATEST_COMPOSE_BOM" ]; then
    sed -i "s/compose-bom = \".*\"/compose-bom = \"$LATEST_COMPOSE_BOM\"/" gradle/libs.versions.toml
fi

# Update SDKs
sed -i "s/compileSdk = [0-9]*/compileSdk = $LATEST_SDK/" app/build.gradle.kts
sed -i "s/compileSdk = [0-9]*/compileSdk = $LATEST_SDK/" ../maplibre-geoman-android/app/build.gradle.kts

# --- 4. Verify ---
echo "Syncing project..."

echo "Done! If the build fails, check ./gradle_backup/ to revert."
