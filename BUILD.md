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

# Configuration
API_URL="http://localhost:5000/api/tokens/generate"
API_KEY="INSECURE-CHANGE-ME-api-key"

# Constraint Parameters
MIN_BUCKETS=5
MAX_BUCKETS=15
MIN_EMAILS_PER_BUCKET=3
MAX_EMAILS_PER_BUCKET=1200

# Arrays for randomized data generation
DOMAINS=("provider.com" "corporate.net" "startup.io" "agency.org" "tech.com" "global.com")
BOOLS=("true" "false")

# 1. Determine total number of buckets
NUM_BUCKETS=$(( RANDOM % (MAX_BUCKETS - MIN_BUCKETS + 1) + MIN_BUCKETS ))

echo "Initialization: Generating $NUM_BUCKETS buckets..."

for (( i=1; i<=NUM_BUCKETS; i++ )); do
    BUCKET_NAME="bucket-$(printf "%03d" $i)"
    
    # 2. Determine number of emails for this specific bucket
    NUM_EMAILS=$(( RANDOM % (MAX_EMAILS_PER_BUCKET - MIN_EMAILS_PER_BUCKET + 1) + MIN_EMAILS_PER_BUCKET ))
    
    echo "Processing [$BUCKET_NAME]: $NUM_EMAILS iterations..."

    for (( j=1; j<=NUM_EMAILS; j++ )); do
        # Generate randomized email data
        USER_ID=$(LC_ALL=C tr -dc 'a-z0-9' < /dev/urandom | head -c 8)
        DOMAIN=${DOMAINS[$RANDOM % ${#DOMAINS[@]}]}
        EMAIL="${USER_ID}@${DOMAIN}"

        # Permission state randomization
        P1=${BOOLS[$RANDOM % 2]}
        P2=${BOOLS[$RANDOM % 2]}
        P3=${BOOLS[$RANDOM % 2]}
        P4=${BOOLS[$RANDOM % 2]}

        # 3. Construct JSON via Template
        PAYLOAD=$(cat <<EOF
{
  "bucket": "$BUCKET_NAME",
  "email": "$EMAIL",
  "permissions": {
    "newsletter": $P1,
    "marketing": $P2,
    "alerts": $P3,
    "promotions": $P4
  }
}
EOF
)

        # 4. Transmit via cURL
        # Silence output and log only HTTP response code
        RESPONSE=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$API_URL" \
            -H "X-Api-Key: $API_KEY" \
            -H "Content-Type: application/json" \
            -d "$PAYLOAD")

        if [[ "$RESPONSE" -ne 200 ]]; then
            echo "Error: Received HTTP $RESPONSE for $EMAIL"
        fi
            
    done
    echo "Finalized [$BUCKET_NAME]."
done

echo "Operation complete."
```