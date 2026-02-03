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
#!/bin/bash

# Define the API endpoint and Key
API_URL="http://localhost:5000/api/tokens/generate"
API_KEY="INSECURE-CHANGE-ME-api-key"

# Array of JSON payloads
payloads=(
  '{"bucket": "q1-campaign", "email": "alice.smith@provider.com", "permissions": {"newsletter": true, "marketing": false, "alerts": true, "promotions": false}}'
  '{"bucket": "q1-campaign", "email": "bob.jones@corporate.net", "permissions": {"newsletter": false, "marketing": true, "alerts": true, "promotions": true}}'
  '{"bucket": "alpha-test", "email": "charlie.davis@startup.io", "permissions": {"newsletter": true, "marketing": true, "alerts": true, "promotions": true}}'
  '{"bucket": "beta-group", "email": "dana.white@agency.org", "permissions": {"newsletter": false, "marketing": false, "alerts": true, "promotions": false}}'
  '{"bucket": "retention", "email": "evan.brown@domain.com", "permissions": {"newsletter": true, "marketing": false, "alerts": false, "promotions": false}}'
  '{"bucket": "monitoring", "email": "frank.miller@tech.com", "permissions": {"newsletter": false, "marketing": false, "alerts": true, "promotions": false}}'
  '{"bucket": "dev-ops", "email": "grace.hopper@system.edu", "permissions": {"newsletter": true, "marketing": false, "alerts": true, "promotions": false}}'
  '{"bucket": "security", "email": "henry.ford@logistics.co", "permissions": {"newsletter": false, "marketing": false, "alerts": true, "promotions": true}}'
  '{"bucket": "support", "email": "iris.west@service.biz", "permissions": {"newsletter": true, "marketing": true, "alerts": true, "promotions": false}}'
  '{"bucket": "logistics", "email": "jack.ryan@global.com", "permissions": {"newsletter": false, "marketing": true, "alerts": false, "promotions": true}}'
  '{"bucket": "sales-leads", "email": "kevin.hart@media.net", "permissions": {"newsletter": true, "marketing": true, "alerts": false, "promotions": true}}'
  '{"bucket": "newsletter-sub", "email": "laura.palmer@press.org", "permissions": {"newsletter": true, "marketing": false, "alerts": false, "promotions": false}}'
  '{"bucket": "black-friday", "email": "mike.ross@retail.com", "permissions": {"newsletter": false, "marketing": true, "alerts": false, "promotions": true}}'
  '{"bucket": "holiday-spec", "email": "nina.simone@music.io", "permissions": {"newsletter": true, "marketing": true, "alerts": true, "promotions": true}}'
  '{"bucket": "early-access", "email": "oscar.isaac@film.com", "permissions": {"newsletter": false, "marketing": true, "alerts": true, "promotions": false}}'
  '{"bucket": "emea-region", "email": "peter.parker@web.com", "permissions": {"newsletter": true, "marketing": false, "alerts": true, "promotions": false}}'
  '{"bucket": "apac-region", "email": "quinn.fabray@asia.net", "permissions": {"newsletter": false, "marketing": true, "alerts": true, "promotions": true}}'
  '{"bucket": "latam-market", "email": "rose.tyler@london.uk", "permissions": {"newsletter": true, "marketing": true, "alerts": false, "promotions": false}}'
  '{"bucket": "finance-dept", "email": "steve.rogers@shield.gov", "permissions": {"newsletter": false, "marketing": false, "alerts": true, "promotions": false}}'
  '{"bucket": "hr-internal", "email": "tina.fey@studio.com", "permissions": {"newsletter": true, "marketing": false, "alerts": false, "promotions": true}}'
  '{"bucket": "guest-user", "email": "uma.thurman@indie.org", "permissions": {"newsletter": false, "marketing": false, "alerts": false, "promotions": false}}'
  '{"bucket": "partner-api", "email": "victor.von@latveria.ru", "permissions": {"newsletter": true, "marketing": true, "alerts": true, "promotions": true}}'
  '{"bucket": "legacy-sync", "email": "wanda.maxim@chaos.io", "permissions": {"newsletter": true, "marketing": false, "alerts": true, "promotions": true}}'
  '{"bucket": "staging-env", "email": "xavier.charles@school.edu", "permissions": {"newsletter": false, "marketing": true, "alerts": false, "promotions": false}}'
  '{"bucket": "archive-list", "email": "yara.shahidi@activist.com", "permissions": {"newsletter": true, "marketing": true, "alerts": false, "promotions": true}}'
)

# Execution Loop
for data in "${payloads[@]}"; do
  echo "Sending request for: $(echo $data | grep -oE '[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}')"
  curl -s -X POST "$API_URL" \
    -H "X-Api-Key: $API_KEY" \
    -H "Content-Type: application/json" \
    -d "$data"
  echo -e "\n---"
done
```