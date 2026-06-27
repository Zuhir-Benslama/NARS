# Code Quality — Issues

| Severity | Issue | Location | Status |
|----------|-------|----------|--------|
| Medium | `FeatureRepository.UpdateFeatureAsync` takes 7 parameters — extract parameter object | `Services/FeatureRepository.cs:41` | Fixed |
| Low | Extra blank line after property block | `Controllers/NarsControllerBase.cs:91` | Fixed |
| Low | Hardcoded OpenTelemetry OTLP endpoint — extract to config | `Infrastructure/ServiceRegistrationExtensions.cs:30` | Fixed |
| Low | Verbose XML comments on trivial getters ("returns null rather than throwing") — simplify or remove | `Controllers/NarsControllerBase.cs` | Fixed |
| Low | `ClearAllFeaturesAsync` builds raw SQL via string concatenation — reviewed, uses parameterized `@uid` | `Services/FeatureRepository.cs:97-145` | Reviewed — safe |
