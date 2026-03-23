#!/bin/bash

# Define the new versions
NEW_AGP="8.6.0"
NEW_SDK="35"
NEW_GRADLE="8.7"

echo "🚀 Starting Android project update..."

# 1. Update Version Catalog (libs.versions.toml) if it exists
if [ -f "gradle/libs.versions.toml" ]; then
    echo "Updating gradle/libs.versions.toml..."
    sed -i.bak "s/agp = \"[^\"]*\"/agp = \"$NEW_AGP\"/" gradle/libs.versions.toml
    sed -i.bak "s/compileSdk = \"[^\"]*\"/compileSdk = \"$NEW_SDK\"/" gradle/libs.versions.toml
    sed -i.bak "s/targetSdk = \"[^\"]*\"/targetSdk = \"$NEW_SDK\"/" gradle/libs.versions.toml
    rm gradle/libs.versions.toml.bak
fi

# 2. Update build.gradle.kts files (Common in modern Android)
echo "Updating build.gradle.kts files..."
find . -name "build.gradle.kts" -exec sed -i.bak "s/compileSdk = [0-9]*/compileSdk = $NEW_SDK/g" {} +
find . -name "build.gradle.kts" -exec sed -i.bak "s/targetSdk = [0-9]*/targetSdk = $NEW_SDK/g" {} +
find . -name "build.gradle.kts" -exec sed -i.bak "s/com.android.application\" version \"[^\"]*\"/com.android.application\" version \"$NEW_AGP\"/g" {} +

# 3. Update build.gradle files (Groovy)
echo "Updating build.gradle files..."
find . -name "build.gradle" -exec sed -i.bak "s/compileSdk [0-9]*/compileSdk $NEW_SDK/g" {} +
find . -name "build.gradle" -exec sed -i.bak "s/targetSdk [0-9]*/targetSdk $NEW_SDK/g" {} +
find . -name "build.gradle" -exec sed -i.bak "s/com.android.tools.build:gradle:[^\']*\'/com.android.tools.build:gradle:$NEW_AGP\'/g" {} +

# Remove backup files created by sed
find . -name "*.bak" -type f -delete

# 4. Update the Gradle Wrapper (Run twice to ensure consistency)
echo "Updating Gradle Wrapper to $NEW_GRADLE..."
if [ -f "./gradlew" ]; then
    ./gradlew wrapper --gradle-version $NEW_GRADLE
    ./gradlew wrapper --gradle-version $NEW_GRADLE
else
    echo "⚠️ gradlew not found. Skipping wrapper update."
fi

echo "✅ Update complete! Running a clean build..."
./gradlew clean

