import json
import csv
import os
from contextlib import asynccontextmanager
from datetime import datetime, timedelta, timezone
from typing import Optional

from fastapi import FastAPI, Depends, HTTPException, status, Request, Response
from fastapi.responses import FileResponse, JSONResponse, RedirectResponse
from fastapi.staticfiles import StaticFiles
from fastapi.middleware.cors import CORSMiddleware

from pydantic import BaseModel, EmailStr
import bcrypt
from jwt import encode as jwt_encode, decode as jwt_decode, PyJWTError

from sqlalchemy.ext.asyncio import AsyncSession, create_async_engine
from sqlalchemy.orm import sessionmaker, declarative_base
from sqlalchemy import Column, Integer, String, Text, DateTime, Float, select, func, distinct, delete
from sqlalchemy.exc import IntegrityError

# ─────────────────────────────────────────────
# Configuration
# ─────────────────────────────────────────────

DATABASE_CONFIG = {
    'host':     os.getenv('DB_HOST',     'localhost'),
    'port':     os.getenv('DB_PORT',     '5432'),
    'database': os.getenv('DB_NAME',     'nars_db'),
    'user':     os.getenv('DB_USER',     'postgres'),
    'password': os.getenv('DB_PASSWORD', 'a21305556699'),   # ← Change this!
}

DATABASE_URL = (
    f"postgresql+asyncpg://{DATABASE_CONFIG['user']}:{DATABASE_CONFIG['password']}"
    f"@{DATABASE_CONFIG['host']}:{DATABASE_CONFIG['port']}/{DATABASE_CONFIG['database']}"
)

SECRET_KEY      = os.getenv('SECRET_KEY', 'change-this-secret-key-in-production')
ALGORITHM       = 'HS256'
ACCESS_TOKEN_EXPIRE_MINUTES = 60 * 24   # 24 hours

# ─────────────────────────────────────────────
# Database Setup
# ─────────────────────────────────────────────

engine = create_async_engine(
    DATABASE_URL,
    pool_size=10,
    max_overflow=20,
    pool_timeout=30,
    pool_recycle=3600,
    echo=False,
)

AsyncSessionLocal = sessionmaker(engine, class_=AsyncSession, expire_on_commit=False)
Base = declarative_base()

# ─────────────────────────────────────────────
# Models (SQLAlchemy)
# ─────────────────────────────────────────────

class UserModel(Base):
    __tablename__ = 'users'
    id            = Column(Integer, primary_key=True, index=True)
    name          = Column(String(255), nullable=False)
    email         = Column(String(255), unique=True, nullable=False, index=True)
    phone         = Column(String(50),  nullable=False)
    username      = Column(String(100), unique=True, nullable=False, index=True)
    password_hash = Column(String(255), nullable=False)
    commune_id    = Column(Integer, nullable=False)
    created_at    = Column(DateTime(timezone=True), default=lambda: datetime.now(timezone.utc))

class FeatureModel(Base):
    __tablename__ = 'features'
    id         = Column(Integer, primary_key=True, index=True)
    type       = Column(String(50),  nullable=False)
    layer      = Column(String(50),  nullable=False)
    label      = Column(String(255), nullable=False)
    data       = Column(Text,        nullable=False)
    created_at = Column(DateTime(timezone=True), default=lambda: datetime.now(timezone.utc))

class WilayaModel(Base):
    __tablename__ = 'wilayas'
    wilaya_id        = Column(Integer, primary_key=True)
    wilaya_ar        = Column(String(50), nullable=False)
    wilaya_fr        = Column(String(50), nullable=False)
    wilaya_latitude  = Column(Float)  # double precision
    wilaya_longitude = Column(Float)  # double precision

class DairaModel(Base):
    __tablename__ = 'dairas'
    daira_id        = Column(Integer, primary_key=True)
    wilaya_id       = Column(Integer, nullable=False, index=True)
    daira_ar        = Column(String(50), nullable=False)
    daira_fr        = Column(String(50), nullable=False)
    daira_latitude  = Column(Float)  # double precision
    daira_longitude = Column(Float)  # double precision
    daira_name      = Column(String(255))

class CommuneModel(Base):
    __tablename__ = 'communes'
    commune_id        = Column(Integer, primary_key=True)
    daira_id          = Column(Integer, nullable=False, index=True)
    commune_code      = Column(String(5))
    commune_ar        = Column(String(50), nullable=False)
    commune_fr        = Column(String(50), nullable=False)
    commune_latitude  = Column(Float)  # double precision
    commune_longitude = Column(Float)  # double precision
    commune_name      = Column(String(255))

class CommuneBoundaryModel(Base):
    __tablename__ = 'communes_boundaries'
    commune_id   = Column(Integer, primary_key=True)
    wkb_geometry = Column(Text, nullable=False)  # PostGIS geometry

# ─────────────────────────────────────────────
# Pydantic Schemas (Request / Response)
# ─────────────────────────────────────────────

class SignUpRequest(BaseModel):
    name:       str
    email:      EmailStr
    phone:      str
    username:   str
    password:   str
    commune_id: int

class SignInRequest(BaseModel):
    username: str
    password: str

class FeatureSaveRequest(BaseModel):
    type:  str
    layer: str
    label: str
    data:  dict

class TokenResponse(BaseModel):
    access_token: str
    token_type:   str = 'bearer'

# ─────────────────────────────────────────────
# Security Helpers
# ─────────────────────────────────────────────

def hash_password(password: str) -> str:
    salt = bcrypt.gensalt()
    hashed = bcrypt.hashpw(password.encode('utf-8'), salt)
    return hashed.decode('utf-8')

def verify_password(plain: str, hashed: str) -> bool:
    try:
        plain_bytes  = plain.encode('utf-8')
        hashed_bytes = hashed.strip().encode('utf-8')
        return bcrypt.checkpw(plain_bytes, hashed_bytes)
    except Exception:
        return False

def create_access_token(data: dict) -> str:
    payload = data.copy()
    payload['exp'] = datetime.now(timezone.utc) + timedelta(minutes=ACCESS_TOKEN_EXPIRE_MINUTES)
    return jwt_encode(payload, SECRET_KEY, algorithm=ALGORITHM)



# ─────────────────────────────────────────────
# DB Session Dependency
# ─────────────────────────────────────────────

async def get_db():
    async with AsyncSessionLocal() as session:
        yield session

# ─────────────────────────────────────────────
# Lifespan: Create Tables & Seed Locations
# ─────────────────────────────────────────────

@asynccontextmanager
async def lifespan(app: FastAPI):
    # ── Startup ──
    print('=' * 50)
    print('NARS - FastAPI + PostgreSQL/PostGIS')
    print('=' * 50)

    async with engine.begin() as conn:
        # Only create tables if they don't exist (features, users)
        # wilaya, daira, communes, communes_boundaries already exist
        await conn.run_sync(Base.metadata.create_all)
    print('✓ Database tables ready')
    print('✓ Location data (wilaya, daira, communes) already in PostgreSQL')
    print('✓ Startup complete — http://localhost:8000\n')

    yield  # ← Application runs here

    # ── Shutdown ──
    await engine.dispose()
    print('✓ Database connections closed')

# ─────────────────────────────────────────────
# App Initialization
# ─────────────────────────────────────────────

app = FastAPI(
    title='NARS - National Addressing Reference System',
    description='Scalable geographic data management API built with FastAPI + PostgreSQL/PostGIS',
    version='2.0.0',
    lifespan=lifespan,
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=['*'],
    allow_credentials=True,
    allow_methods=['*'],
    allow_headers=['*'],
)

# Serve static files (JS)
app.mount('/static', StaticFiles(directory='.'), name='static')


# ─────────────────────────────────────────────
# Page Routes (Serve HTML)
# ─────────────────────────────────────────────

@app.get('/', include_in_schema=False)
async def root(request: Request):
    # Check for token cookie to decide redirect
    token = request.cookies.get('access_token')
    if token:
        return RedirectResponse('/map')
    return RedirectResponse('/login')

@app.get('/login', include_in_schema=False)
async def login_page():
    return FileResponse('login.html')

@app.get('/map', include_in_schema=False)
async def map_page(request: Request):
    token = request.cookies.get('access_token')
    if not token:
        return RedirectResponse('/login')
    return FileResponse('map_app.html')

@app.get('/app.js', include_in_schema=False)
async def serve_js():
    return FileResponse('app.js')

@app.get('/Boundaries.geojson', include_in_schema=False)
async def serve_boundaries():
    return FileResponse('Boundaries.geojson')

# ─────────────────────────────────────────────
# Auth Routes
# ─────────────────────────────────────────────

@app.post('/api/signup', status_code=201, tags=['Auth'])
async def signup(body: SignUpRequest, db: AsyncSession = Depends(get_db)):
    """Register a new user."""
    existing = await db.execute(
        select(UserModel).where(
            (UserModel.username == body.username) | (UserModel.email == body.email)
        )
    )
    existing = existing.scalar_one_or_none()
    if existing:
        field = 'Username' if existing.username == body.username else 'Email'
        raise HTTPException(status_code=409, detail=f'{field} already exists')

    user = UserModel(
        name          = body.name,
        email         = body.email,
        phone         = body.phone,
        username      = body.username,
        password_hash = hash_password(body.password),
        commune_id    = body.commune_id,
    )
    db.add(user)
    await db.commit()
    await db.refresh(user)
    return {'success': True, 'message': 'User registered successfully', 'user_id': user.id}


@app.post('/api/signin', tags=['Auth'])
async def signin(body: SignInRequest, response: Response, db: AsyncSession = Depends(get_db)):
    """Sign in and receive a JWT token (also set as cookie)."""
    result = await db.execute(select(UserModel).where(UserModel.username == body.username))
    user = result.scalar_one_or_none()

    if not user or not verify_password(body.password, user.password_hash):
        raise HTTPException(status_code=401, detail='Invalid username or password')

    token = create_access_token({
        'user_id':    user.id,
        'username':   user.username,
        'name':       user.name,
        'email':      user.email,
        'commune_id': user.commune_id,
    })

    # Set token as HTTP-only cookie (for page-based auth)
    response.set_cookie(
        key='access_token',
        value=token,
        httponly=True,
        max_age=60 * 60 * 24,   # 24 hours
        samesite='lax',
    )

    # Fetch commune with its daira and wilaya
    commune_result = await db.execute(select(CommuneModel).where(CommuneModel.commune_id == user.commune_id))
    commune = commune_result.scalar_one_or_none()
    
    # Get daira and wilaya info from commune
    daira = None
    wilaya = None
    if commune:
        daira_result = await db.execute(select(DairaModel).where(DairaModel.daira_id == commune.daira_id))
        daira = daira_result.scalar_one_or_none()
        
        if daira:
            wilaya_result = await db.execute(select(WilayaModel).where(WilayaModel.wilaya_id == daira.wilaya_id))
            wilaya = wilaya_result.scalar_one_or_none()

    return {
        'success':      True,
        'access_token': token,
        'token_type':   'bearer',
        'user': {
            'id':       user.id,
            'username': user.username,
            'name':     user.name,
            'email':    user.email,
            'commune':  {
                'id': user.commune_id, 
                'name_fr': commune.commune_fr if commune else None,
                'name_ar': commune.commune_ar if commune else None,
                'latitude': commune.commune_latitude if commune else None,
                'longitude': commune.commune_longitude if commune else None,
            },
        },
    }


@app.post('/api/logout', tags=['Auth'])
async def logout(response: Response):
    """Clear session cookie."""
    response.delete_cookie('access_token')
    return {'success': True, 'message': 'Logged out successfully'}


@app.get('/api/current_user', tags=['Auth'])
async def current_user_info(request: Request, db: AsyncSession = Depends(get_db)):
    """Get current logged-in user from cookie token with full location data."""
    token = request.cookies.get('access_token')
    if not token:
        raise HTTPException(status_code=401, detail='Not authenticated')
    try:
        payload = jwt_decode(token, SECRET_KEY, algorithms=[ALGORITHM])
    except PyJWTError:
        raise HTTPException(status_code=401, detail='Invalid token')

    user_id = payload.get('user_id')
    commune_id = payload.get('commune_id')

    # Fetch commune with its daira and wilaya through joins
    commune_result = await db.execute(select(CommuneModel).where(CommuneModel.commune_id == commune_id))
    commune = commune_result.scalar_one_or_none()
    
    # Get daira and wilaya info from commune
    daira = None
    wilaya = None
    if commune:
        daira_result = await db.execute(select(DairaModel).where(DairaModel.daira_id == commune.daira_id))
        daira = daira_result.scalar_one_or_none()
        
        if daira:
            wilaya_result = await db.execute(select(WilayaModel).where(WilayaModel.wilaya_id == daira.wilaya_id))
            wilaya = wilaya_result.scalar_one_or_none()

    return {
        'id':       user_id,
        'username': payload.get('username'),
        'name':     payload.get('name'),
        'email':    payload.get('email'),
        'wilaya':   {
            'id': wilaya.wilaya_id if wilaya else None,
            'name_fr': wilaya.wilaya_fr if wilaya else None,
            'name_ar': wilaya.wilaya_ar if wilaya else None,
            'latitude': wilaya.wilaya_latitude if wilaya else None,
            'longitude': wilaya.wilaya_longitude if wilaya else None,
        },
        'daira':    {
            'id': daira.daira_id if daira else None,
            'name_fr': daira.daira_fr if daira else None,
            'name_ar': daira.daira_ar if daira else None,
            'latitude': daira.daira_latitude if daira else None,
            'longitude': daira.daira_longitude if daira else None,
        },
        'commune':  {
            'id': commune_id,
            'name_fr': commune.commune_fr if commune else None,
            'name_ar': commune.commune_ar if commune else None,
            'latitude': commune.commune_latitude if commune else None,
            'longitude': commune.commune_longitude if commune else None,
        },
    }

# ─────────────────────────────────────────────
# Location Routes
# ─────────────────────────────────────────────

@app.get('/api/wilayas', tags=['Locations'])
async def get_wilayas(search: str = '', db: AsyncSession = Depends(get_db)):
    """Get all wilayas with IDs, optionally filtered."""
    q = select(WilayaModel)
    if search:
        q = q.where(
            (WilayaModel.wilaya_fr.ilike(f'%{search}%')) | 
            (WilayaModel.wilaya_ar.ilike(f'%{search}%'))
        )
    result = await db.execute(q.order_by(WilayaModel.wilaya_fr))
    return [
        {
            'id': w.wilaya_id,
            'name_fr': w.wilaya_fr,
            'name_ar': w.wilaya_ar,
            'latitude': w.wilaya_latitude,
            'longitude': w.wilaya_longitude
        }
        for w in result.scalars().all()
    ]


@app.get('/api/dairas', tags=['Locations'])
async def get_dairas(wilaya_id: int, search: str = '', db: AsyncSession = Depends(get_db)):
    """Get dairas for a wilaya, optionally filtered."""
    q = select(DairaModel).where(DairaModel.wilaya_id == wilaya_id)
    if search:
        q = q.where(
            (DairaModel.daira_fr.ilike(f'%{search}%')) | 
            (DairaModel.daira_ar.ilike(f'%{search}%'))
        )
    result = await db.execute(q.order_by(DairaModel.daira_fr))
    return [
        {
            'id': d.daira_id,
            'name_fr': d.daira_fr,
            'name_ar': d.daira_ar,
            'latitude': d.daira_latitude,
            'longitude': d.daira_longitude,
            'full_name': d.daira_name
        }
        for d in result.scalars().all()
    ]


@app.get('/api/communes', tags=['Locations'])
async def get_communes(daira_id: int, search: str = '', db: AsyncSession = Depends(get_db)):
    """Get communes for a daira, optionally filtered."""
    q = select(CommuneModel).where(CommuneModel.daira_id == daira_id)
    if search:
        q = q.where(
            (CommuneModel.commune_fr.ilike(f'%{search}%')) | 
            (CommuneModel.commune_ar.ilike(f'%{search}%'))
        )
    result = await db.execute(q.order_by(CommuneModel.commune_fr))
    return [
        {
            'id': c.commune_id,
            'name_fr': c.commune_fr,
            'name_ar': c.commune_ar,
            'code': c.commune_code,
            'latitude': c.commune_latitude,
            'longitude': c.commune_longitude,
            'full_name': c.commune_name
        }
        for c in result.scalars().all()
    ]


@app.get('/api/commune/{commune_id}/boundary-debug', tags=['Locations'])
async def debug_commune_boundary(commune_id: int, db: AsyncSession = Depends(get_db)):
    """Debug endpoint to check boundary geometry format."""
    result = await db.execute(
        select(CommuneBoundaryModel).where(CommuneBoundaryModel.commune_id == commune_id)
    )
    boundary = result.scalar_one_or_none()
    
    if not boundary:
        return {'error': 'Boundary not found'}
    
    geom = boundary.wkb_geometry
    return {
        'commune_id': commune_id,
        'geometry_type': type(geom).__name__,
        'geometry_length': len(str(geom)),
        'geometry_preview': str(geom)[:200],
        'starts_with': str(geom)[:20],
        'is_json_like': str(geom).strip().startswith('{'),
        'full_geometry': geom  # Full geometry data
    }


@app.get('/api/commune/{commune_id}/boundary', tags=['Locations'])
async def get_commune_boundary(commune_id: int, db: AsyncSession = Depends(get_db)):
    """Get boundary geometry for a specific commune (converts WKB to GeoJSON)."""
    from sqlalchemy import text
    
    # First, get commune info
    commune_result = await db.execute(
        select(CommuneModel).where(CommuneModel.commune_id == commune_id)
    )
    commune = commune_result.scalar_one_or_none()
    
    # Convert WKB geometry to GeoJSON using PostGIS
    try:
        result = await db.execute(
            text("""
                SELECT ST_AsGeoJSON(wkb_geometry) as geojson
                FROM communes_boundaries 
                WHERE commune_id = :commune_id
            """),
            {"commune_id": commune_id}
        )
        row = result.fetchone()
        
        if not row or not row[0]:
            raise HTTPException(status_code=404, detail='Boundary not found for this commune')
        
        return {
            'commune_id': commune_id,
            'commune_name': commune.commune_fr if commune else None,
            'geometry': row[0]  # GeoJSON string from PostGIS
        }
        
    except Exception as e:
        # Log the error for debugging
        print(f"Error converting WKB to GeoJSON: {e}")
        raise HTTPException(
            status_code=500, 
            detail=f'Failed to convert boundary geometry: {str(e)}'
        )

# ─────────────────────────────────────────────
# Feature Routes
# ─────────────────────────────────────────────

def _feature_to_dict(f: FeatureModel) -> dict:
    return {
        'id':         f.id,
        'type':       f.type,
        'layer':      f.layer,
        'label':      f.label,
        'data':       json.loads(f.data),
        'created_at': f.created_at.isoformat() if f.created_at else None,
    }

async def _require_auth(request: Request) -> dict:
    """Cookie-based auth guard for feature endpoints."""
    token = request.cookies.get('access_token')
    if not token:
        raise HTTPException(status_code=401, detail='Not authenticated')
    try:
        return jwt_decode(token, SECRET_KEY, algorithms=[ALGORITHM])
    except PyJWTError:
        raise HTTPException(status_code=401, detail='Invalid token')


@app.post('/api/save', status_code=201, tags=['Features'])
async def save_feature(
    body: FeatureSaveRequest,
    request: Request,
    db: AsyncSession = Depends(get_db),
):
    await _require_auth(request)
    feature = FeatureModel(
        type  = body.type,
        layer = body.layer,
        label = body.label,
        data  = json.dumps(body.data),
    )
    db.add(feature)
    await db.commit()
    await db.refresh(feature)
    return {'success': True, 'id': feature.id, 'message': 'Feature saved successfully'}


@app.get('/api/load', tags=['Features'])
async def load_features(request: Request, db: AsyncSession = Depends(get_db)):
    await _require_auth(request)
    result = await db.execute(select(FeatureModel).order_by(FeatureModel.created_at))
    return [_feature_to_dict(f) for f in result.scalars().all()]


@app.post('/api/clear', tags=['Features'])
async def clear_features(request: Request, db: AsyncSession = Depends(get_db)):
    await _require_auth(request)
    result = await db.execute(delete(FeatureModel))
    await db.commit()
    return {'success': True, 'message': f'Deleted {result.rowcount} features'}


@app.delete('/api/delete/{feature_id}', tags=['Features'])
async def delete_feature(feature_id: int, request: Request, db: AsyncSession = Depends(get_db)):
    await _require_auth(request)
    result = await db.execute(select(FeatureModel).where(FeatureModel.id == feature_id))
    feature = result.scalar_one_or_none()
    if not feature:
        raise HTTPException(status_code=404, detail='Feature not found')
    await db.delete(feature)
    await db.commit()
    return {'success': True, 'message': 'Feature deleted successfully'}


@app.get('/api/stats', tags=['Features'])
async def get_stats(request: Request, db: AsyncSession = Depends(get_db)):
    await _require_auth(request)
    result = await db.execute(
        select(FeatureModel.type, func.count(FeatureModel.id)).group_by(FeatureModel.type)
    )
    stats = {row[0]: row[1] for row in result.fetchall()}
    total = await db.execute(select(func.count()).select_from(FeatureModel))
    stats['total'] = total.scalar()
    return stats


@app.get('/api/load/layer/{layer_type}', tags=['Features'])
async def load_by_layer(layer_type: str, request: Request, db: AsyncSession = Depends(get_db)):
    await _require_auth(request)
    result = await db.execute(
        select(FeatureModel)
        .where(FeatureModel.layer == layer_type)
        .order_by(FeatureModel.created_at)
    )
    return [_feature_to_dict(f) for f in result.scalars().all()]


@app.get('/api/load/type/{feature_type}', tags=['Features'])
async def load_by_type(feature_type: str, request: Request, db: AsyncSession = Depends(get_db)):
    await _require_auth(request)
    result = await db.execute(
        select(FeatureModel)
        .where(FeatureModel.type == feature_type)
        .order_by(FeatureModel.created_at)
    )
    return [_feature_to_dict(f) for f in result.scalars().all()]


# ─────────────────────────────────────────────
# Run
# ─────────────────────────────────────────────

if __name__ == '__main__':
    import uvicorn
    uvicorn.run('main:app', host='0.0.0.0', port=8000, reload=True)
