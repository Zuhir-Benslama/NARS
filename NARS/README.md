# NARS – ASP.NET Core + Vite/Vue 3

**ASP.NET Core 10** backend + **Vite/Vue 3** frontend.

---

## Project Layout

```
/
├── nars-vite/          ← Vite/Vue 3 frontend
│   ├── index.html      ← Map SPA entry (served at /map)
│   ├── login.html      ← Login page (served at /login)
│   ├── vite.config.js  ← outDir → ../NARS/wwwroot
│   └── src/
│       ├── main.js
│       ├── App.vue
│       ├── api.js / map.js / store.js / phases.js / validation.js
│       └── components/*.vue
│
└── NARS/               ← ASP.NET Core 10 backend
    ├── Program.cs
    ├── NarsApi.csproj
    ├── appsettings.json
    ├── wwwroot/        ← Vite build output lands here (npm run build)
    ├── Controllers/
    ├── Data/
    ├── DTOs/
    ├── Models/
    └── Services/
```

---

## Development Workflow

### 1. Start the backend

```bash
cd NARS
dotnet run
# → http://localhost:5000
```

### 2. Start the Vite dev server (separate terminal)

```bash
cd nars-vite
npm install
npm run dev
# → http://localhost:5173
# All /api/* calls are proxied to localhost:5000 automatically
```

Open **http://localhost:5173** in your browser during development.

---

## Production Build

```bash
cd nars-vite
npm run build
# Vite bundles the app and writes directly to ../NARS/wwwroot/
```

Then run the backend as normal — it will serve the built frontend from `wwwroot/`:

```bash
cd NARS
dotnet run
# → http://localhost:5000  (serves both API and frontend)
```

---

## Configuration

Edit `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=nars_db;Username=postgres;Password=YOUR_PASSWORD"
  },
  "Jwt": {
    "SecretKey": "change-this-secret-key-in-production",
    "ExpiresInMinutes": 1440
  },
  "Cors": {
    "AllowedOrigins": [
      "http://localhost:5000",
      "http://localhost:5173"
    ]
  }
}
```

---

## API Routes

| Controller       | Route                                   | Description                         |
|------------------|-----------------------------------------|-------------------------------------|
| Auth             | POST /api/signup                        | Register                            |
| Auth             | POST /api/signin                        | Login (sets HttpOnly cookie)        |
| Auth             | POST /api/logout                        | Clear cookie                        |
| Auth             | GET  /api/current_user                  | Get logged-in user info             |
| Features         | POST /api/save                          | Save a map feature                  |
| Features         | GET  /api/load                          | Load all features for current user  |
| Features         | PUT  /api/update/{id}                   | Update feature geometry/label       |
| Features         | DELETE /api/delete/{id}                 | Delete a feature                    |
| Features         | GET  /api/stats                         | Feature counts by type              |
| Validation       | POST /api/validate/road                 | Validate road geometry              |
| Validation       | POST /api/validate/district             | Validate district geometry          |
| Validation       | GET  /api/validate/districts/coverage   | Check district coverage             |
| Validation       | GET  /api/validate/area/main-urban-exists | Check if main urban area exists   |
| Validation       | POST /api/road-side                     | Determine entrance side + number    |
| Validation       | POST /api/areas/refresh-scattered       | Recompute scattered areas           |
| Locations        | GET  /api/wilayas                       | Search wilayas                      |
| Locations        | GET  /api/dairas                        | Search dairas by wilaya             |
| Locations        | GET  /api/communes                      | Search communes by daira            |
| Locations        | GET  /api/commune/{id}/boundary         | GeoJSON boundary for a commune      |
| Pages            | GET  /                                  | Redirect to /map or /login          |
| Pages            | GET  /login                             | Serve login.html                    |
| Pages            | GET  /map                               | Serve index.html (auth-guarded)     |
