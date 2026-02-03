========================================
      BEACON CONSENT SERVICE
========================================

Lightweight consent and opt-out service for .NET 10.

PREREQUISITES
=============

** REQUIRED: .NET 10.0 Runtime **
Download from: https://dotnet.microsoft.com/download/dotnet/10.0
Install "ASP.NET Core Runtime 10.0.x" for Windows x64

QUICK START
===========

1. TEST MODE (Recommended First)
   Run: Beacon.bat test
   Stop: Press Ctrl+C

2. INSTALL AS WINDOWS SERVICE
   Run as Administrator: Beacon.bat install
   Start service: Beacon.bat start

CONFIGURATION
=============

Required environment variables (set before running):

  Beacon__SigningKey      HMAC signing key for token validation
  Beacon__EncryptionKey   AES-256 master key for data encryption
  Beacon__Pepper          Email hashing pepper

Optional:

  Beacon__DatabaseProvider   sqlite, sqlserver, postgres, mysql
                             (default: sqlite)
  Beacon__ConnectionString   Database connection string
                             (default: Data Source=Beacon.db)

Or edit appsettings.json for additional configuration.

SERVICE MANAGEMENT
==================

Beacon.bat commands:
  install   - Install Windows service (requires Admin)
  start     - Start the service
  stop      - Stop the service
  restart   - Restart the service
  status    - Show status and recent logs
  uninstall - Remove Windows service (requires Admin)
  test      - Run in console mode

FOLDER STRUCTURE
================

log/         - Application logs
.core/       - Encryption keys (DO NOT DELETE)
license/     - License information
Beacon.db    - SQLite database (default)

TROUBLESHOOTING
===============

Service won't start:
1. Run: Beacon.bat test (check console errors)
2. Check logs in: log/
3. Ensure required environment variables are set

SECURITY NOTES
==============

- Set all required environment variables before production use
- Use HTTPS in production (reverse proxy recommended)
- Backup Beacon.db and .core/ folder regularly
- Limit network access appropriately

LICENSE
=======

AGPL-3.0-or-later
See license/LICENSE.txt

SUPPORT
=======

Issues: https://github.com/melosso/beacon/issues
