from flask import Flask, request, jsonify, send_from_directory, session, redirect, url_for
import json
import os
import csv
from werkzeug.security import generate_password_hash, check_password_hash
from functools import wraps
from sqlalchemy import create_engine, Column, Integer, String, Text, DateTime, func
from sqlalchemy.ext.declarative import declarative_base
from sqlalchemy.orm import sessionmaker, scoped_session
from datetime import datetime

app = Flask(__name__, static_folder='.')
app.secret_key = 'your-secret-key-change-this-in-production'  # Change this to a random secret key

# PostgreSQL Database Configuration
# Update these with your PostgreSQL credentials
DATABASE_CONFIG = {
    'host': 'localhost',
    'port': 5432,
    'database': 'nars_db',
    'user': 'postgres',
    'password': 'CHANGE_ME_DB_PASSWORD'  # Change this!
}

# Create database connection string
DATABASE_URL = f"postgresql://{DATABASE_CONFIG['user']}:{DATABASE_CONFIG['password']}@{DATABASE_CONFIG['host']}:{DATABASE_CONFIG['port']}/{DATABASE_CONFIG['database']}"

# Create SQLAlchemy engine with connection pooling
engine = create_engine(
    DATABASE_URL,
    pool_size=10,          # Number of permanent connections
    max_overflow=20,       # Number of additional connections when pool is full
    pool_timeout=30,       # Seconds to wait for connection
    pool_recycle=3600,     # Recycle connections after 1 hour
    echo=False             # Set to True for SQL debugging
)

# Create session factory
SessionLocal = scoped_session(sessionmaker(autocommit=False, autoflush=False, bind=engine))
Base = declarative_base()

# Database Models
class Feature(Base):
    __tablename__ = 'features'
    
    id = Column(Integer, primary_key=True)
    type = Column(String(50), nullable=False)
    layer = Column(String(50), nullable=False)
    label = Column(String(255), nullable=False)
    data = Column(Text, nullable=False)
    created_at = Column(DateTime, default=datetime.utcnow)

class User(Base):
    __tablename__ = 'users'
    
    id = Column(Integer, primary_key=True)
    name = Column(String(255), nullable=False)
    email = Column(String(255), unique=True, nullable=False)
    phone = Column(String(50), nullable=False)
    username = Column(String(100), unique=True, nullable=False)
    password_hash = Column(String(255), nullable=False)
    wilaya = Column(String(100), nullable=False)
    daira = Column(String(100), nullable=False)
    commune = Column(String(100), nullable=False)
    created_at = Column(DateTime, default=datetime.utcnow)

class Location(Base):
    __tablename__ = 'locations'
    
    id = Column(Integer, primary_key=True)
    wilaya = Column(String(100), nullable=False)
    daira = Column(String(100), nullable=False)
    commune = Column(String(100), nullable=False)

# Login required decorator
def login_required(f):
    @wraps(f)
    def decorated_function(*args, **kwargs):
        if 'user_id' not in session:
            return redirect(url_for('login'))
        return f(*args, **kwargs)
    return decorated_function

def init_db():
    """Initialize the PostgreSQL database"""
    try:
        # Create all tables
        Base.metadata.create_all(bind=engine)
        print("✓ Database tables created successfully!")
        
        # Check if locations table is empty
        db = SessionLocal()
        try:
            locations_count = db.query(Location).count()
            
            if locations_count == 0:
                # Load data from CSV
                print("Loading locations from CSV...")
                with open('algeria_cities.csv', 'r', encoding='utf-8') as file:
                    reader = csv.DictReader(file)
                    locations = []
                    for row in reader:
                        location = Location(
                            wilaya=row['wilaya_name_ascii'],
                            daira=row['daira_name_ascii'],
                            commune=row['commune_name_ascii']
                        )
                        locations.append(location)
                    
                    db.bulk_save_objects(locations)
                    db.commit()
                    print(f"✓ Loaded {len(locations)} locations into database")
            else:
                print(f"✓ Locations table already populated ({locations_count} records)")
            
        finally:
            db.close()
        
        print("✓ Database initialization complete!")
        
    except Exception as e:
        print(f"✗ Database initialization error: {e}")
        raise

# Routes
@app.route('/')
def index():
    """Redirect to login or map based on session"""
    if 'user_id' in session:
        return redirect(url_for('map_page'))
    return redirect(url_for('login'))

@app.route('/login')
def login():
    """Serve the login page"""
    return send_from_directory('.', 'login.html')

@app.route('/map')
@login_required
def map_page():
    """Serve the main HTML file"""
    return send_from_directory('.', 'map_app.html')

@app.route('/app.js')
def serve_js():
    """Serve the JavaScript file"""
    return send_from_directory('.', 'app.js')

@app.route('/Boundaries.geojson')
def serve_boundaries():
    """Serve the Boundaries GeoJSON file"""
    return send_from_directory('.', 'Boundaries.geojson')

# Location API Endpoints
@app.route('/api/wilayas', methods=['GET'])
def get_wilayas():
    """Get all unique wilayas, optionally filtered by search query"""
    try:
        search = request.args.get('search', '').strip()
        
        db = SessionLocal()
        try:
            query = db.query(Location.wilaya).distinct()
            
            if search:
                query = query.filter(Location.wilaya.ilike(f'%{search}%'))
            
            wilayas = [row[0] for row in query.order_by(Location.wilaya).all()]
            return jsonify(wilayas), 200
            
        finally:
            db.close()
            
    except Exception as e:
        return jsonify({'error': str(e)}), 500

@app.route('/api/dairas', methods=['GET'])
def get_dairas():
    """Get dairas for a specific wilaya, optionally filtered by search query"""
    try:
        wilaya = request.args.get('wilaya', '').strip()
        search = request.args.get('search', '').strip()
        
        if not wilaya:
            return jsonify({'error': 'Wilaya parameter is required'}), 400
        
        db = SessionLocal()
        try:
            query = db.query(Location.daira).filter(Location.wilaya == wilaya).distinct()
            
            if search:
                query = query.filter(Location.daira.ilike(f'%{search}%'))
            
            dairas = [row[0] for row in query.order_by(Location.daira).all()]
            return jsonify(dairas), 200
            
        finally:
            db.close()
            
    except Exception as e:
        return jsonify({'error': str(e)}), 500

@app.route('/api/communes', methods=['GET'])
def get_communes():
    """Get communes for a specific wilaya and daira, optionally filtered by search query"""
    try:
        wilaya = request.args.get('wilaya', '').strip()
        daira = request.args.get('daira', '').strip()
        search = request.args.get('search', '').strip()
        
        if not wilaya or not daira:
            return jsonify({'error': 'Wilaya and daira parameters are required'}), 400
        
        db = SessionLocal()
        try:
            query = db.query(Location.commune).filter(
                Location.wilaya == wilaya,
                Location.daira == daira
            ).distinct()
            
            if search:
                query = query.filter(Location.commune.ilike(f'%{search}%'))
            
            communes = [row[0] for row in query.order_by(Location.commune).all()]
            return jsonify(communes), 200
            
        finally:
            db.close()
            
    except Exception as e:
        return jsonify({'error': str(e)}), 500

# Authentication API Endpoints
@app.route('/api/signup', methods=['POST'])
def signup():
    """Register a new user"""
    try:
        data = request.json
        
        # Validate required fields
        required_fields = ['name', 'email', 'phone', 'username', 'password', 'wilaya', 'daira', 'commune']
        for field in required_fields:
            if field not in data or not data[field]:
                return jsonify({'error': f'Missing required field: {field}'}), 400
        
        # Hash password
        password_hash = generate_password_hash(data['password'])
        
        db = SessionLocal()
        try:
            # Check if username or email already exists
            existing_user = db.query(User).filter(
                (User.username == data['username']) | (User.email == data['email'])
            ).first()
            
            if existing_user:
                if existing_user.username == data['username']:
                    return jsonify({'error': 'Username already exists'}), 409
                else:
                    return jsonify({'error': 'Email already exists'}), 409
            
            # Create new user
            new_user = User(
                name=data['name'],
                email=data['email'],
                phone=data['phone'],
                username=data['username'],
                password_hash=password_hash,
                wilaya=data['wilaya'],
                daira=data['daira'],
                commune=data['commune']
            )
            
            db.add(new_user)
            db.commit()
            db.refresh(new_user)
            
            return jsonify({
                'success': True,
                'message': 'User registered successfully',
                'user_id': new_user.id
            }), 201
            
        finally:
            db.close()
            
    except Exception as e:
        return jsonify({'error': str(e)}), 500

@app.route('/api/signin', methods=['POST'])
def signin():
    """Sign in an existing user"""
    try:
        data = request.json
        
        if 'username' not in data or 'password' not in data:
            return jsonify({'error': 'Missing username or password'}), 400
        
        db = SessionLocal()
        try:
            user = db.query(User).filter(User.username == data['username']).first()
            
            if user and check_password_hash(user.password_hash, data['password']):
                # Create session
                session['user_id'] = user.id
                session['username'] = user.username
                session['name'] = user.name
                session['email'] = user.email
                session['wilaya'] = user.wilaya
                session['daira'] = user.daira
                session['commune'] = user.commune
                
                return jsonify({
                    'success': True,
                    'message': 'Signed in successfully',
                    'user': {
                        'id': user.id,
                        'username': user.username,
                        'name': user.name,
                        'email': user.email,
                        'wilaya': user.wilaya,
                        'daira': user.daira,
                        'commune': user.commune
                    }
                }), 200
            else:
                return jsonify({'error': 'Invalid username or password'}), 401
                
        finally:
            db.close()
            
    except Exception as e:
        return jsonify({'error': str(e)}), 500

@app.route('/api/logout', methods=['POST'])
def logout():
    """Log out the current user"""
    session.clear()
    return jsonify({'success': True, 'message': 'Logged out successfully'}), 200

@app.route('/api/current_user', methods=['GET'])
@login_required
def get_current_user():
    """Get current logged-in user's information"""
    return jsonify({
        'id': session.get('user_id'),
        'username': session.get('username'),
        'name': session.get('name'),
        'email': session.get('email'),
        'wilaya': session.get('wilaya'),
        'daira': session.get('daira'),
        'commune': session.get('commune')
    }), 200

# Feature API Endpoints
@app.route('/api/save', methods=['POST'])
@login_required
def save_feature():
    """Save a new feature to the database"""
    try:
        data = request.json
        
        db = SessionLocal()
        try:
            feature = Feature(
                type=data['type'],
                layer=data['layer'],
                label=data['label'],
                data=json.dumps(data['data'])
            )
            
            db.add(feature)
            db.commit()
            db.refresh(feature)
            
            return jsonify({
                'success': True,
                'id': feature.id,
                'message': 'Feature saved successfully'
            }), 201
            
        finally:
            db.close()
            
    except Exception as e:
        return jsonify({'error': str(e)}), 500

@app.route('/api/load', methods=['GET'])
@login_required
def load_features():
    """Load all features from the database"""
    try:
        db = SessionLocal()
        try:
            features = db.query(Feature).order_by(Feature.created_at).all()
            
            result = []
            for feature in features:
                result.append({
                    'id': feature.id,
                    'type': feature.type,
                    'layer': feature.layer,
                    'label': feature.label,
                    'data': json.loads(feature.data),
                    'created_at': feature.created_at.isoformat() if feature.created_at else None
                })
            
            return jsonify(result), 200
            
        finally:
            db.close()
            
    except Exception as e:
        return jsonify({'error': str(e)}), 500

@app.route('/api/clear', methods=['POST'])
@login_required
def clear_features():
    """Clear all features from the database"""
    try:
        db = SessionLocal()
        try:
            deleted_count = db.query(Feature).delete()
            db.commit()
            
            return jsonify({
                'success': True,
                'message': f'Deleted {deleted_count} features'
            }), 200
            
        finally:
            db.close()
            
    except Exception as e:
        return jsonify({'error': str(e)}), 500

@app.route('/api/delete/<int:feature_id>', methods=['DELETE'])
@login_required
def delete_feature(feature_id):
    """Delete a specific feature"""
    try:
        db = SessionLocal()
        try:
            feature = db.query(Feature).filter(Feature.id == feature_id).first()
            
            if feature:
                db.delete(feature)
                db.commit()
                return jsonify({
                    'success': True,
                    'message': 'Feature deleted successfully'
                }), 200
            else:
                return jsonify({'error': 'Feature not found'}), 404
                
        finally:
            db.close()
            
    except Exception as e:
        return jsonify({'error': str(e)}), 500

@app.route('/api/stats', methods=['GET'])
@login_required
def get_stats():
    """Get feature statistics"""
    try:
        db = SessionLocal()
        try:
            stats = {}
            
            # Count by type
            type_counts = db.query(
                Feature.type,
                func.count(Feature.id)
            ).group_by(Feature.type).all()
            
            for feature_type, count in type_counts:
                stats[feature_type] = count
            
            # Total count
            stats['total'] = db.query(Feature).count()
            
            return jsonify(stats), 200
            
        finally:
            db.close()
            
    except Exception as e:
        return jsonify({'error': str(e)}), 500

@app.route('/api/load/layer/<layer_type>', methods=['GET'])
@login_required
def load_by_layer(layer_type):
    """Load features by layer type"""
    try:
        db = SessionLocal()
        try:
            features = db.query(Feature).filter(
                Feature.layer == layer_type
            ).order_by(Feature.created_at).all()
            
            result = []
            for feature in features:
                result.append({
                    'id': feature.id,
                    'type': feature.type,
                    'layer': feature.layer,
                    'label': feature.label,
                    'data': json.loads(feature.data),
                    'created_at': feature.created_at.isoformat() if feature.created_at else None
                })
            
            return jsonify(result), 200
            
        finally:
            db.close()
            
    except Exception as e:
        return jsonify({'error': str(e)}), 500

@app.route('/api/load/type/<feature_type>', methods=['GET'])
@login_required
def load_by_type(feature_type):
    """Load features by specific type"""
    try:
        db = SessionLocal()
        try:
            features = db.query(Feature).filter(
                Feature.type == feature_type
            ).order_by(Feature.created_at).all()
            
            result = []
            for feature in features:
                result.append({
                    'id': feature.id,
                    'type': feature.type,
                    'layer': feature.layer,
                    'label': feature.label,
                    'data': json.loads(feature.data),
                    'created_at': feature.created_at.isoformat() if feature.created_at else None
                })
            
            return jsonify(result), 200
            
        finally:
            db.close()
            
    except Exception as e:
        return jsonify({'error': str(e)}), 500

if __name__ == '__main__':
    print("=" * 50)
    print("NARS - National Addressing Reference System")
    print("PostgreSQL Backend Server")
    print("=" * 50)
    
    # Initialize database
    init_db()
    
    print("\n" + "=" * 50)
    print("Server starting on http://localhost:5000")
    print("=" * 50 + "\n")
    
    app.run(debug=True, host='0.0.0.0', port=5000)
