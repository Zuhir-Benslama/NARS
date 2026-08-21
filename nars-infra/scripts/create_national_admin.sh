#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# create_national_admin.sh
#
# Creates the NARS national_admin account directly in the PostgreSQL database.
# This is the only way to bootstrap the first admin — no API endpoint exists
# for national_admin creation by design.
#
# All user-supplied values are passed as psycopg2 parameters — never
# interpolated into SQL strings, so there is no SQL injection risk.
#
# Requirements: python3, psycopg2-binary, bcrypt (auto-installed if missing)
#
# Usage:
#   chmod +x create_national_admin.sh
#   ./create_national_admin.sh
#
# Optional env overrides (interactive if unset):
#   NARS_DB_HOST  NARS_DB_PORT  NARS_DB_NAME  NARS_DB_USER  NARS_DB_PASSWORD
#
# Non-interactive mode (no prompts; requires NON_INTERACTIVE=1):
#   NON_INTERACTIVE=1  ADMIN_USERNAME  ADMIN_PASSWORD  ADMIN_NAME
#   ADMIN_EMAIL  ADMIN_PHONE
#   All ADMIN_* values fall back to generated credentials when omitted.
#   The generated password is printed to stderr only.
#
# Security notes:
#   - The DB password is never passed as a command-line argument.
#     It flows via NARS_DB_PASSWORD_VAL env var read by Python heredocs.
#   - All user-supplied values are bound as psycopg2 %s parameters, never
#     interpolated into SQL strings.
# ─────────────────────────────────────────────────────────────────────────────

set -euo pipefail

RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'
CYAN='\033[0;36m'; BOLD='\033[1m'; RESET='\033[0m'
info()    { echo -e "${CYAN}[INFO]${RESET}  $*"; }
success() { echo -e "${GREEN}[OK]${RESET}    $*"; }
warn()    { echo -e "${YELLOW}[WARN]${RESET}  $*"; }
die()     { echo -e "${RED}[ERROR]${RESET} $*" >&2; exit 1; }

echo -e "${BOLD}╔══════════════════════════════════════════════════════════╗${RESET}"
echo -e "${BOLD}║       NARS — National Admin Account Creation             ║${RESET}"
echo -e "${BOLD}╚══════════════════════════════════════════════════════════╝${RESET}"
echo ""

command -v python3 >/dev/null 2>&1 || die "python3 not found."

# Auto-install Python dependencies into a temporary virtualenv (avoids
# --break-system-packages and keeps system Python clean).
VENV_DIR=""
for pkg in psycopg2-binary bcrypt; do
    module="${pkg%%-*}"
    if ! python3 -c "import sys; __import__(sys.argv[1])" "${module}" 2>/dev/null; then
        if [[ -z "${VENV_DIR}" ]]; then
            VENV_DIR=$(mktemp -d)
            python3 -m venv "${VENV_DIR}"
            # shellcheck disable=SC1091
            source "${VENV_DIR}/bin/activate"
        fi
        warn "Python package '${pkg}' not found. Installing..."
        if pip install "${pkg}" --quiet; then
            success "${pkg} installed."
        else
            die "Failed to install ${pkg}. Run: pip install ${pkg}"
        fi
    fi
done
# Use the venv python for all subsequent calls, even if no install was needed
if [[ -n "${VENV_DIR}" ]]; then
    PYTHON="${VENV_DIR}/bin/python3"
else
    PYTHON="python3"
fi

# ── Database connection ────────────────────────────────────────────────────────
DB_HOST="${NARS_DB_HOST:-localhost}"
DB_PORT="${NARS_DB_PORT:-5432}"
DB_NAME="${NARS_DB_NAME:-nars_db}"
DB_USER="${NARS_DB_USER:-postgres}"

if [[ -z "${NARS_DB_PASSWORD:-}" ]]; then
    read -r -s -p "$(echo -e "${CYAN}PostgreSQL password for ${DB_USER}@${DB_HOST}: ${RESET}")" DB_PASSWORD
    echo ""
else
    DB_PASSWORD="${NARS_DB_PASSWORD}"
fi

# Export for Python heredocs — avoids CLI-arg exposure in process listing
export NARS_DB_PASSWORD_VAL="${DB_PASSWORD}"
export NARS_DB_HOST="$DB_HOST"
export NARS_DB_PORT="$DB_PORT"
export NARS_DB_NAME="$DB_NAME"
export NARS_DB_USER="$DB_USER"

# ── Shared Python database helper ─────────────────────────────────────────────
# Temp module with connect_db() so we don't duplicate the connection logic.
DB_HELPER_DIR=$(mktemp -d)
trap 'rm -rf "${DB_HELPER_DIR}" "${VENV_DIR}"' EXIT INT TERM HUP

cat > "${DB_HELPER_DIR}/db_helper.py" << 'PYHELP'
import os, psycopg2

def connect_db():
    return psycopg2.connect(
        host=os.environ["NARS_DB_HOST"],
        port=int(os.environ["NARS_DB_PORT"]),
        dbname=os.environ["NARS_DB_NAME"],
        user=os.environ["NARS_DB_USER"],
        password=os.environ["NARS_DB_PASSWORD_VAL"],
    )
PYHELP

# ── Test connection ──────────────────────────────────────────────────────────────
info "Testing database connection to ${DB_HOST}:${DB_PORT}/${DB_NAME}..."
PYTHONPATH="${DB_HELPER_DIR}" "${PYTHON}" -c "from db_helper import connect_db; connect_db().close()" \
    || die "Cannot connect. Check credentials and that PostgreSQL is running."
success "Database connection OK."
echo ""

# ── Check for existing national_admin ─────────────────────────────────────────
EXISTING=$(PYTHONPATH="${DB_HELPER_DIR}" "${PYTHON}" - <<'PYEOF'
from db_helper import connect_db

conn = connect_db()
cur = conn.cursor()
cur.execute("SELECT username FROM users WHERE role = 'national_admin' LIMIT 5")
for row in cur.fetchall():
    print(row[0])
conn.close()
PYEOF
)

if [[ -n "${EXISTING}" ]]; then
    warn "A national_admin account already exists:"
    echo "${EXISTING}" | while read -r u; do echo "    • ${u}"; done
    echo ""
    read -r -p "$(echo -e "${YELLOW}Continue and create another? [y/N]: ${RESET}")" CONTINUE
    [[ "${CONTINUE,,}" == "y" ]] || { info "Aborted."; exit 0; }
    echo ""
fi

# ── Collect account details ────────────────────────────────────────────────────
if [[ "${NON_INTERACTIVE:-0}" == "1" ]]; then
    ADMIN_NAME="${ADMIN_NAME:-National Admin}"
    ADMIN_EMAIL="${ADMIN_EMAIL:-admin@nars.dz}"
    ADMIN_PHONE="${ADMIN_PHONE:-+213000000000}"
    ADMIN_USERNAME="${ADMIN_USERNAME:-admin_$(openssl rand -hex 4)}"
    ADMIN_PASSWORD="${ADMIN_PASSWORD:-$(openssl rand -base64 12)}"
    echo -e "${CYAN}[INFO]${RESET}  Non-interactive mode — generating one-time credentials"
else
    echo -e "${BOLD}Enter details for the new national admin account:${RESET}"
    echo ""

    read -r -p "  Full name:     " ADMIN_NAME
    [[ -n "${ADMIN_NAME}" ]] || die "Name cannot be empty."

    read -r -p "  Email:         " ADMIN_EMAIL
    [[ "${ADMIN_EMAIL}" =~ ^[^@]+@[^@]+\.[^@]+$ ]] || die "Invalid email address."

    read -r -p "  Phone:         " ADMIN_PHONE
    [[ -n "${ADMIN_PHONE}" ]] || die "Phone cannot be empty."

    read -r -p "  Username:      " ADMIN_USERNAME
    [[ -n "${ADMIN_USERNAME}" ]] || die "Username cannot be empty."
    [[ "${#ADMIN_USERNAME}" -ge 3 ]] || die "Username must be at least 3 characters."

    while true; do
        read -r -s -p "  Password:      " ADMIN_PASSWORD; echo ""
        [[ "${#ADMIN_PASSWORD}" -ge 8 ]] || { warn "Password must be at least 8 characters."; continue; }
        read -r -s -p "  Confirm:       " ADMIN_CONFIRM; echo ""
        [[ "${ADMIN_PASSWORD}" == "${ADMIN_CONFIRM}" ]] && break
        warn "Passwords do not match. Try again."
    done
    echo ""
fi

# ── Confirmation (skip in non-interactive mode) ──────────────────────────────
if [[ "${NON_INTERACTIVE:-0}" != "1" ]]; then
    echo -e "${BOLD}Review:${RESET}"
    echo "  Role:     national_admin"
    echo "  Name:     ${ADMIN_NAME}"
    echo "  Email:    ${ADMIN_EMAIL}"
    echo "  Phone:    ${ADMIN_PHONE}"
    echo "  Username: ${ADMIN_USERNAME}"
    echo ""
    read -r -p "$(echo -e "${YELLOW}Proceed? [y/N]: ${RESET}")" CONFIRM
    [[ "${CONFIRM,,}" == "y" ]] || { info "Aborted — no changes made."; exit 0; }
fi

# ── Insert via Python with parameterised query ─────────────────────────────────
# All user-supplied values are bound as psycopg2 %s parameters — they are never
# interpolated into the SQL string. DB connection params and admin details are
# passed via env vars, scoped to a subshell to avoid leaking to /proc.
#
# UUID: uuid.uuid4() generates a random UUID. The application normally uses
# Guid.CreateVersion7() (time-ordered) for new rows, but the national_admin
# is a bootstrap account — sequential ordering is not required here.
info "Hashing password and inserting record..."

set +e
NEW_UUID=$(
    export NARS_ADMIN_PASSWORD_VAL="${ADMIN_PASSWORD}"
    export NARS_ADMIN_NAME="${ADMIN_NAME}"
    export NARS_ADMIN_EMAIL="${ADMIN_EMAIL}"
    export NARS_ADMIN_PHONE="${ADMIN_PHONE}"
    export NARS_ADMIN_USERNAME="${ADMIN_USERNAME}"
    PYTHONPATH="${DB_HELPER_DIR}" "${PYTHON}" - << 'PYEOF'
import sys, os, uuid, bcrypt
from db_helper import connect_db

name    = os.environ["NARS_ADMIN_NAME"]
email   = os.environ["NARS_ADMIN_EMAIL"]
phone   = os.environ["NARS_ADMIN_PHONE"]
username = os.environ["NARS_ADMIN_USERNAME"]
password = os.environ["NARS_ADMIN_PASSWORD_VAL"]

new_id   = str(uuid.uuid4())
pwd_hash = bcrypt.hashpw(password.encode(), bcrypt.gensalt(rounds=11)).decode()
# Matches User.GenerateSecurityStamp() (Guid.NewGuid().ToString("N")).
# Required: OnTokenValidated rejects tokens whose security_stamp claim is
# empty, so a row created without this column can sign in but never pass
# authentication.
security_stamp = uuid.uuid4().hex

try:
    conn = connect_db()
    cur  = conn.cursor()

    cur.execute("SELECT 1 FROM users WHERE username = %s OR email = %s LIMIT 1", (username, email))
    if cur.fetchone():
        print("DUPE", file=sys.stderr)
        conn.close(); sys.exit(2)

    cur.execute("""
        INSERT INTO users (
            id, name, email, phone, username, password_hash,
            role, commune_id, daira_id, wilaya_id,
            created_at, failed_login_attempts, locked_until,
            security_stamp
        ) VALUES (
            %s, %s, %s, %s, %s, %s,
            'national_admin', NULL, NULL, NULL,
            NOW(), 0, NULL,
            %s
        )
    """, (new_id, name, email, phone, username, pwd_hash, security_stamp))

    conn.commit(); conn.close()
    print(new_id)

except Exception as e:
    print(f"DB_ERROR: {e}", file=sys.stderr); sys.exit(1)
PYEOF
)
PYEXIT=$?
set -e

# Clear sensitive env vars used by the subshell
unset NARS_ADMIN_PASSWORD_VAL NARS_ADMIN_NAME NARS_ADMIN_EMAIL NARS_ADMIN_PHONE NARS_ADMIN_USERNAME

if   [[ ${PYEXIT} -eq 2 ]]; then
    die "Username '${ADMIN_USERNAME}' or email '${ADMIN_EMAIL}' is already in use."
elif [[ ${PYEXIT} -ne 0 ]]; then
    die "Insert failed. See error above."
fi

echo ""
success "National admin account created successfully!"
echo ""
echo -e "${BOLD}Account details:${RESET}"
echo "  UUID:     ${NEW_UUID}"
echo "  Username: ${ADMIN_USERNAME}"
if [[ "${NON_INTERACTIVE:-0}" == "1" ]]; then
    # Print password to stderr so it isn't captured if stdout is piped/logged.
    echo "  Password: ${ADMIN_PASSWORD}" >&2
    echo "" >&2
    echo -e "${YELLOW}⚠  Save these credentials now. They will not be shown again.${RESET}" >&2
fi
echo "  Role:     national_admin"
echo ""
# Clear DB credentials from environment
unset NARS_DB_PASSWORD_VAL ADMIN_PASSWORD
echo -e "${GREEN}You can now sign in at /login and create wilaya admins.${RESET}"
