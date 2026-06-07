# NARStreet — Commands for AI-assisted development

## Build
```bash
./gradlew :app:assembleDebug
```

## Test
```bash
# All unit tests
./gradlew :app:testDebugUnitTest

# Specific test class
./gradlew :app:testDebugUnitTest --tests "com.nars.maplibre.modes.NarsGeomanTest"
```

## Static Analysis
```bash
# Detekt
./gradlew detekt

# Android lint
./gradlew :app:lint
```

## Clean
```bash
./gradlew clean
```
