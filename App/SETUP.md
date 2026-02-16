# NARS Setup Instructions

## ⚠️ IMPORTANT - Database Changes

This version uses **TWO SEPARATE databases**:
1. **`map_features.db`** - For map features and location data
2. **`vault.sqlite`** - For user authentication data (NEW)

## 🔧 First-Time Setup

1. Install dependencies:
```bash
pip install flask --break-system-packages
```

2. Ensure all files are in the same directory:
   - login.html
   - map_app.html
   - app.js
   - server.py
   - algeria_cities.csv
   - Boundaries.geojson
   - requirements.txt

3. Run the server:
```bash
python server.py
```

4. Open your browser to: `http://localhost:5000`

## 🔄 Upgrading from Previous Version

If you previously ran this application, you need to handle the database migration:

### Option 1: Fresh Start (Recommended)
Delete the old database files and let the system create new ones:
```bash
rm map_features.db vault.sqlite
python server.py
```
**Note:** This will delete all existing users and map features!

### Option 2: Keep Map Features, Reset Users
1. Keep your map features:
```bash
# Backup your features if needed
cp map_features.db map_features_backup.db
```

2. Delete user data (if it exists in old database):
```bash
# The new system uses vault.sqlite for users
rm vault.sqlite
```

3. Run the server:
```bash
python server.py
```

## 📊 Database Structure

### vault.sqlite (User Data)
```sql
CREATE TABLE users (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    email TEXT UNIQUE NOT NULL,          -- NEW: Email field
    phone TEXT NOT NULL,
    username TEXT UNIQUE NOT NULL,
    password_hash TEXT NOT NULL,
    wilaya TEXT NOT NULL,
    daira TEXT NOT NULL,                 -- NOW INCLUDED: Daira column
    commune TEXT NOT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);
```

### map_features.db (Map Data)
- **features** table: Stores drawn map features
- **locations** table: Stores wilaya/daira/commune data from CSV

## ✅ New Sign-Up Fields

The registration form now includes:
1. Full Name
2. **Email** (NEW - must be unique)
3. Phone Number
4. Username (must be unique)
5. Password (securely hashed)
6. Wilaya (searchable dropdown)
7. **Daira** (searchable dropdown, filtered by wilaya)
8. Commune (searchable dropdown, filtered by daira)

## 🐛 Troubleshooting

### Error: "table users has no column named daira"
**Solution:** You have an old database file. Delete both database files and restart:
```bash
rm map_features.db vault.sqlite
python server.py
```

### Error: "Username already exists" or "Email already exists"
**Solution:** Either:
- Use a different username/email, or
- Delete vault.sqlite to reset all users

### Error: "algeria_cities.csv not found"
**Solution:** Make sure `algeria_cities.csv` is in the same directory as server.py

### Error: "Boundaries.geojson not found"
**Solution:** Make sure `Boundaries.geojson` is in the same directory as server.py

## 📝 Testing the Sign-Up

1. Go to `http://localhost:5000`
2. Click "Sign Up" tab
3. Fill in all fields:
   - Type in Wilaya field to search (e.g., "Adrar", "Alger")
   - Select wilaya from dropdown
   - Type in Daira field to search within selected wilaya
   - Select daira from dropdown
   - Type in Commune field to search within selected daira
   - Select commune from dropdown
4. Click "Sign Up"
5. You should see "Account created successfully!"
6. Sign in with your credentials

## 🔐 Security Notes

- Passwords are hashed using Werkzeug (never stored in plain text)
- Sessions are managed securely
- Change the secret key in server.py for production use:
  ```python
  app.secret_key = 'your-secret-key-change-this-in-production'
  ```

## 📞 Support

If you encounter any issues:
1. Check that all files are in the correct directory
2. Delete database files and restart fresh
3. Check console output for error messages
4. Ensure Python 3.7+ is installed
