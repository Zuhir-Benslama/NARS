# ─── Backend (legacy standalone build — prefer Dockerfile.nars-api) ────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0-bookworm-slim AS build
WORKDIR /src
COPY NARS/NarsApi.csproj ./
RUN dotnet restore
COPY NARS/ ./
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-bookworm-slim AS final
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish ./
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
  CMD curl -f http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "NarsApi.dll"]
