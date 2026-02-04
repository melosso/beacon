-- ============================================================================
-- Beacon - SQL Server Database Setup Script
-- ============================================================================
-- This script creates the database, login, and user required for Beacon.
-- EF Core will automatically create the tables on first run.
--
-- Run this script as a sysadmin (sa) or a user with CREATE DATABASE permission.
-- ============================================================================

USE [master];
GO

-- ============================================================================
-- 1. Create the Database
-- ============================================================================
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = N'Beacon')
BEGIN
    CREATE DATABASE [Beacon]
    COLLATE Latin1_General_100_CI_AS_SC_UTF8;
    PRINT 'Database [Beacon] created successfully.';
END
ELSE
BEGIN
    PRINT 'Database [Beacon] already exists.';
END
GO

-- ============================================================================
-- 2. Create the Login
-- ============================================================================
-- Change 'YourSecurePassword123!' to a strong password
IF NOT EXISTS (SELECT name FROM sys.server_principals WHERE name = N'beacon_app')
BEGIN
    CREATE LOGIN [beacon_app]
    WITH PASSWORD = N'YourSecurePassword123!',
         DEFAULT_DATABASE = [Beacon],
         CHECK_EXPIRATION = OFF,
         CHECK_POLICY = ON;
    PRINT 'Login [beacon_app] created successfully.';
END
ELSE
BEGIN
    PRINT 'Login [beacon_app] already exists.';
END
GO

-- ============================================================================
-- 3. Create the Database User and Assign Permissions
-- ============================================================================
USE [Beacon];
GO

IF NOT EXISTS (SELECT name FROM sys.database_principals WHERE name = N'beacon_app')
BEGIN
    CREATE USER [beacon_app] FOR LOGIN [beacon_app];
    PRINT 'User [beacon_app] created successfully.';
END
ELSE
BEGIN
    PRINT 'User [beacon_app] already exists.';
END
GO

-- Grant permissions required for EF Core to create and manage schema
-- db_ddladmin: CREATE, ALTER, DROP tables/indexes
-- db_datareader: SELECT
-- db_datawriter: INSERT, UPDATE, DELETE
ALTER ROLE [db_ddladmin] ADD MEMBER [beacon_app];
ALTER ROLE [db_datareader] ADD MEMBER [beacon_app];
ALTER ROLE [db_datawriter] ADD MEMBER [beacon_app];
GO

PRINT 'Permissions assigned to [beacon_app].';
PRINT '';
PRINT '============================================================================';
PRINT 'Setup complete! Beacon will create tables automatically on first run.';
PRINT '============================================================================';
GO

-- ============================================================================
-- Connection String for appsettings.json
-- ============================================================================
/*
SQL Server 2022+ / Azure SQL Connection String:

"ConnectionString": "Server=localhost;Database=Beacon;User Id=beacon_app;Password=YourSecurePassword123!;TrustServerCertificate=True;Encrypt=True"

Alternative formats:

-- Named instance:
"ConnectionString": "Server=localhost\\SQLEXPRESS;Database=Beacon;User Id=beacon_app;Password=YourSecurePassword123!;TrustServerCertificate=True;Encrypt=True"

-- Custom port:
"ConnectionString": "Server=localhost,1433;Database=Beacon;User Id=beacon_app;Password=YourSecurePassword123!;TrustServerCertificate=True;Encrypt=True"

-- Windows Authentication (Integrated Security):
"ConnectionString": "Server=localhost;Database=Beacon;Integrated Security=True;TrustServerCertificate=True;Encrypt=True"

-- Azure SQL with Entra ID (formerly AAD):
"ConnectionString": "Server=yourserver.database.windows.net;Database=Beacon;Authentication=Active Directory Default;Encrypt=True"

Don't forget to also set:
"DatabaseProvider": "sqlserver"
*/
