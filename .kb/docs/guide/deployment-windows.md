---
title: Deploying on Windows Server
description: "Install Beacon on Windows Server as a Windows service"
---

# Deploying on Windows Server

## Installation

### 1. Extract the release

Download `Beacon-<tag>-win-x64.zip` from [Releases](https://github.com/melosso/beacon/releases) and extract the archive.

### 2. Install the .NET 11 runtime

```powershell
winget install --id Microsoft.DotNet.RunTime.11 -e
```

### 3. Generate the keys

Run in the Beacon folder:

```powershell
if (Test-Path .env) { throw ".env already exists" }
function New-Key { $b = New-Object byte[] 32; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); [Convert]::ToBase64String($b) }
$adminKey = New-Key
"SIGNING_KEY=$(New-Key)", "ENCRYPTION_KEY=$(New-Key)", "PEPPER=$(New-Key)", "ADMIN_API_KEY=$adminKey" | Set-Content .env
Write-Host "Your API key is: $adminKey"
```

### 4. Start the service

Run as Administrator:

```powershell
.\Beacon.bat install
.\Beacon.bat start
```

### 5. Verify

```powershell
Invoke-RestMethod http://localhost:5000/health
```
