### From Source

```bash
dotnet build Source/Beacon.slnx
dotnet run --project Source/Beacon
```

- API: http://localhost:5000
- Admin Panel: http://localhost:5001

### Development Secrets

Production refuses to start on the shipped placeholder keys. Keep real ones out of the repository:

```bash
cd Source/Beacon
dotnet user-secrets set "Beacon:SigningKey"    "$(openssl rand -base64 32)"
dotnet user-secrets set "Beacon:EncryptionKey" "$(openssl rand -base64 32)"
dotnet user-secrets set "Beacon:Pepper"        "$(openssl rand -base64 32)"
dotnet user-secrets set "Beacon:AdminApiKey"   "$(openssl rand -base64 36 | tr -d '\n')"
```

These secrets are then stored outside the working tree. Environment variables and a `.env` beside the binary work too. Every setting accepts three names, canonical first: `Beacon__SigningKey`, `BEACON_SIGNING_KEY`, `SigningKey`.

### Test

```bash
dotnet test Source/Beacon.slnx
```

Provider tests need Docker. They spin up SQL Server, PostgreSQL and MySQL via Testcontainers.

### Database Providers

```bash
BEACON_DATABASE_PROVIDER=postgres BEACON_CONNECTION_STRING="Host=localhost;Database=beacon;Username=postgres;Password=..." dotnet run --project Source/Beacon
```

Accepts `sqlite`, `sqlserver`, `postgres`, `mysql`.

### Publish

E.g. for Windows instances:

```bash
dotnet publish ./Source/Beacon/Beacon.csproj \
  -c Release \
  -r win-x64 \
  --self-contained false \
  -p:PublishSingleFile=true \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  --framework net10.0 \
  -o ./Deployment
```

### Test Data

```bash
We've moved this to POPULATE.sh for a more elaborate script.
```
