### From Source
```bash
dotnet build Source/Beacon.sln
dotnet run --project Source/Beacon
```

- API: http://localhost:5000
- Admin Panel: http://localhost:5001

### Publish

dotnet publish ./Source/Beacon/Beacon.csproj \
  -c Release \
  -r win-x64 \
  --self-contained false \
  -p:PublishSingleFile=true \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  --framework net10.0 \
  -o ./Deployment

### Test Data
```bash
I've moved this to POPULATE.sh for a more elaborate script.
```