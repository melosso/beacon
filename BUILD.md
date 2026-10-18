### From Source

```bash
dotnet build Source/Beacon.slnx
dotnet run --project Source/Beacon
```

- API: http://localhost:5000
- Admin Panel: http://localhost:5001

### Secrets

Production refuses to start with the placeholder keys from `appsettings.json`. Generate a `.env` (gitignored):

```bash
cd Source/Beacon
if [ -f .env ]; then echo ".env already exists. Aborting."; else ADMIN_KEY=$(openssl rand -base64 48 | tr -d '\n'); printf "SIGNING_KEY=%s\nENCRYPTION_KEY=%s\nPEPPER=%s\nADMIN_API_KEY=%s\n" "$(openssl rand -base64 32)" "$(openssl rand -base64 32)" "$(openssl rand -base64 32)" "$ADMIN_KEY" > .env && chmod 600 .env && echo "Your X-Api-Key is: $ADMIN_KEY"; fi
```

Beacon reads `.env` from the binary folder and the working directory. Environment variables take precedence over `.env`. See [CONFIGURATION.md](CONFIGURATION.md#naming) for accepted names.

### Test

```bash
dotnet test --solution Source/Beacon.slnx
```

Provider tests need Docker. They spin up SQL Server, PostgreSQL and MySQL via Testcontainers.

### Database Providers

```bash
DATABASE_PROVIDER=postgres CONNECTION_STRING="Host=localhost;Database=beacon;Username=postgres;Password=..." dotnet run --project Source/Beacon
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
