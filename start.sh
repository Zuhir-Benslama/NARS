#!/bin/bash
# ─────────────────────────────────────────────────────────────────────────────
# NARS — First-time setup & run script
# Run from the project root (the folder that contains both NARS/ and nars-vite/)
#
#   chmod +x start.sh
#   ./start.sh
# ─────────────────────────────────────────────────────────────────────────────

set -e  # exit on any error

# ── Colors ────────────────────────────────────────────────────────────────────
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
CYAN='\033[0;36m'
BOLD='\033[1m'
NC='\033[0m' # No Color

ok()   { echo -e "${GREEN}✓${NC} $1"; }
info() { echo -e "${CYAN}→${NC} $1"; }
warn() { echo -e "${YELLOW}⚠${NC}  $1"; }
fail() { echo -e "${RED}✗ ERROR:${NC} $1"; exit 1; }

echo ""
echo -e "${BOLD}══════════════════════════════════════════${NC}"
echo -e "${BOLD}   NARS — National Addressing Reference System ${NC}"
echo -e "${BOLD}══════════════════════════════════════════${NC}"
echo ""

# ── Step 1: Check we're in the right folder ───────────────────────────────────
info "Checking project structure..."

[ -d "nars-vite" ]  || fail "nars-vite/ folder not found. Run this script from the project root."
[ -d "NARS" ]       || fail "NARS/ folder not found. Run this script from the project root."
[ -f "nars-vite/src/api.js" ] || fail "nars-vite/src/api.js not found."

ok "Project structure looks good"

# ── Step 2: Check prerequisites ───────────────────────────────────────────────
info "Checking prerequisites..."

command -v node   >/dev/null 2>&1 || fail "Node.js is not installed. Download from https://nodejs.org"
command -v npm    >/dev/null 2>&1 || fail "npm is not installed. It comes with Node.js."
command -v dotnet >/dev/null 2>&1 || fail ".NET SDK is not installed. Download from https://dotnet.microsoft.com/download"

NODE_VER=$(node -v)
DOTNET_VER=$(dotnet --version)
ok "Node.js $NODE_VER"
ok ".NET SDK $DOTNET_VER"

# ── Step 3: Fix credentials bug in api.js ────────────────────────────────────
info "Checking api.js credentials setting..."

API_FILE="nars-vite/src/api.js"

if grep -q "credentials: 'include'" "$API_FILE"; then
    ok "api.js credentials already set to 'include'"
elif grep -q "credentials: 'same-origin'" "$API_FILE"; then
    sed -i "s/credentials: 'same-origin'/credentials: 'include'/" "$API_FILE"
    ok "Fixed api.js: credentials 'same-origin' → 'include'"
else
    warn "Could not auto-fix api.js. Please open $API_FILE and make sure it has: credentials: 'include'"
fi

# ── Step 4: Ask dev or production ─────────────────────────────────────────────
echo ""
echo -e "${BOLD}How do you want to run the app?${NC}"
echo "  1) Development  — Vite dev server (hot reload) + backend API"
echo "  2) Production   — Build frontend, serve everything from the backend"
echo ""
read -rp "Enter 1 or 2: " MODE

if [[ "$MODE" != "1" && "$MODE" != "2" ]]; then
    fail "Invalid choice. Please run the script again and enter 1 or 2."
fi

# ── Step 5: Install frontend dependencies ─────────────────────────────────────
echo ""
info "Installing frontend dependencies (npm install)..."
cd nars-vite
npm install
ok "npm packages installed"
cd ..

# ── Step 6: Restore backend packages ─────────────────────────────────────────
info "Restoring backend packages (dotnet restore)..."
cd NARS
dotnet restore
ok "dotnet packages restored"
cd ..

# ─────────────────────────────────────────────────────────────────────────────
# MODE 1 — DEVELOPMENT
# ─────────────────────────────────────────────────────────────────────────────
if [[ "$MODE" == "1" ]]; then

    echo ""
    echo -e "${BOLD}Starting in development mode...${NC}"
    echo -e "  Backend  → ${CYAN}http://localhost:5000${NC}"
    echo -e "  Frontend → ${CYAN}http://localhost:5173${NC}  ← open this in your browser"
    echo ""
    echo -e "${YELLOW}Press Ctrl+C to stop both servers${NC}"
    echo ""

    # Start backend in background
    cd NARS
    dotnet run &
    BACKEND_PID=$!
    cd ..

    # Give the backend a moment to start
    sleep 3

    # Start Vite dev server in foreground
    cd nars-vite
    npm run dev &
    FRONTEND_PID=$!
    cd ..

    # Wait and handle Ctrl+C cleanly
    trap "echo ''; info 'Shutting down...'; kill $BACKEND_PID $FRONTEND_PID 2>/dev/null; ok 'Stopped.'; exit 0" SIGINT SIGTERM

    wait $FRONTEND_PID
    kill $BACKEND_PID 2>/dev/null

# ─────────────────────────────────────────────────────────────────────────────
# MODE 2 — PRODUCTION
# ─────────────────────────────────────────────────────────────────────────────
else

    echo ""
    info "Building frontend for production..."
    cd nars-vite
    npm run build
    ok "Frontend built → NARS/wwwroot/"
    cd ..

    echo ""
    echo -e "${BOLD}Starting backend (production mode)...${NC}"
    echo -e "  App → ${CYAN}http://localhost:5000${NC}  ← open this in your browser"
    echo ""
    echo -e "${YELLOW}Press Ctrl+C to stop${NC}"
    echo ""

    cd NARS
    dotnet run

fi
