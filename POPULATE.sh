#!/bin/bash

# ======================================
# Configuration
# ======================================
API_URL="http://localhost:5000/api/tokens/generate"
API_KEY="INSECURE-CHANGE-ME-api-key"

MIN_EMAILS_PER_BUCKET=5
MAX_EMAILS_PER_BUCKET=300

DOMAINS=(
  "unternehmen.eu"
  "firma.de"
  "societe.fr"
  "azienda.it"
  "empresa.es"
  "organisatie.nl"
)

# ======================================
# Permission Universe (6 total)
# ======================================
PERMISSION_KEYS=(
  "Marketing"
  "Services"
  "Newsletter"
  "Alerts"
  "Promotions"
  "Compliance"
)

# ======================================
# Buckets
# ======================================
BUCKETS=(
  "marketing_campaign_dach_spring_2026"
  "marketing_campaign_nordics_sustainability_2026"
  "marketing_campaign_nordics_sustainability_2027"
  "marketing_campaign_southern_europe_summer_2026"
  "product_update_eu_platform_q2_2026"
  "mobile_app_update_eu_release"
  "cloud_service_maintenance_eu"
  "internal_engineering_berlin"
  "internal_sales_operations_paris"
  "internal_customer_support_lisbon"
  "customer_loyalty_programme_eu"
  "partner_enablement_network_eu"
  "early_access_programme_eu"
  "gdpr_mandatory_notifications_eu"
  "privacy_policy_updates_eu"
  "terms_of_service_changes_eu"
)

# ======================================
# Functions
# ======================================

generate_email_permissions() {
    local min=2
    local max=6
    local true_count=$(( RANDOM % (max - min + 1) + min ))

    # Randomly select permissions to enable
    mapfile -t ENABLED < <(
        printf '%s\n' "${PERMISSION_KEYS[@]}" \
        | shuf \
        | head -n "$true_count"
    )

    # Build JSON true/false map
    local json=""
    for perm in "${PERMISSION_KEYS[@]}"; do
        if printf '%s\n' "${ENABLED[@]}" | grep -qx "$perm"; then
            json+="\"$perm\": true,"
        else
            json+="\"$perm\": false,"
        fi
    done

    echo "${json%,}"
}

# ======================================
# Processing
# ======================================

echo "Initialization: Generating ${#BUCKETS[@]} buckets..."

for BUCKET_NAME in "${BUCKETS[@]}"; do

    NUM_EMAILS=$(( RANDOM % (MAX_EMAILS_PER_BUCKET - MIN_EMAILS_PER_BUCKET + 1) + MIN_EMAILS_PER_BUCKET ))

    echo "Processing [$BUCKET_NAME]: $NUM_EMAILS users..."

    for (( i=1; i<=NUM_EMAILS; i++ )); do
        USER_ID=$(LC_ALL=C tr -dc 'a-z0-9' < /dev/urandom | head -c 8)
        DOMAIN=${DOMAINS[$RANDOM % ${#DOMAINS[@]}]}
        EMAIL="${USER_ID}@${DOMAIN}"

        PERMISSIONS_JSON=$(generate_email_permissions)

        PAYLOAD=$(cat <<EOF
[{
  "bucket": "$BUCKET_NAME",
  "email": "$EMAIL",
  "permissions": {
    $PERMISSIONS_JSON
  }
}]
EOF
)

        RESPONSE=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$API_URL" \
            -H "X-Api-Key: $API_KEY" \
            -H "Content-Type: application/json" \
            -d "$PAYLOAD")

        if [[ "$RESPONSE" -ne 200 ]]; then
            echo "Error: HTTP $RESPONSE for $EMAIL in [$BUCKET_NAME]"
        fi
    done

    echo "Finalized [$BUCKET_NAME]"
    echo "--------------------------------------"
done

# ======================================
# Submission Forms
# ======================================

ADMIN_URL="http://localhost:5001/api/admin/submissions"
SUBSCRIBE_BASE="http://localhost:5000/api/submission"

echo "Creating submission forms..."

# Create newsletter_nl form
NL_RESPONSE=$(curl -s -X POST "$ADMIN_URL" \
    -H "X-Api-Key: $API_KEY" \
    -H "Content-Type: application/json" \
    -d '{
        "name": "Dutch Newsletter",
        "bucket": "newsletter_nl",
        "permission": "Newsletter",
        "allowedOrigins": ["http://localhost:5000", "http://localhost:5001", "http://localhost:8070"],
        "language": "nl",
        "isEnabled": true,
        "formConfig": {
            "title": "Schrijf je in voor de nieuwsbrief",
            "description": "Ontvang updates in je inbox.",
            "buttonText": "Inschrijven",
            "successMessage": "Bedankt voor je inschrijving!"
        }
    }')

NL_FORM_ID=$(echo "$NL_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
echo "Created newsletter_nl form: $NL_FORM_ID"

# Create newsletter_de form
DE_RESPONSE=$(curl -s -X POST "$ADMIN_URL" \
    -H "X-Api-Key: $API_KEY" \
    -H "Content-Type: application/json" \
    -d '{
        "name": "German Newsletter",
        "bucket": "newsletter_de",
        "permission": "Newsletter",
        "allowedOrigins": ["http://localhost:5000", "http://localhost:5001", "http://localhost:8070"],
        "language": "de",
        "isEnabled": true,
        "formConfig": {
            "title": "Newsletter abonnieren",
            "description": "Erhalten Sie Updates direkt in Ihr Postfach.",
            "buttonText": "Abonnieren",
            "successMessage": "Vielen Dank für Ihre Anmeldung!"
        }
    }')

DE_FORM_ID=$(echo "$DE_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
echo "Created newsletter_de form: $DE_FORM_ID"

# Subscribe 2 emails to newsletter_nl
if [[ -n "$NL_FORM_ID" ]]; then
    echo "Subscribing 2 emails to newsletter_nl..."
    for email in "jan.devries@organisatie.nl" "sophie.bakker@organisatie.nl"; do
        curl -s -o /dev/null -w "  %{http_code} $email\n" -X POST "$SUBSCRIBE_BASE/$NL_FORM_ID/subscribe" \
            -H "Content-Type: application/json" \
            -H "Origin: http://localhost:8070" \
            -d "{\"email\": \"$email\", \"consent\": \"true\"}"
    done
fi

# Subscribe 3 emails to newsletter_de
if [[ -n "$DE_FORM_ID" ]]; then
    echo "Subscribing 3 emails to newsletter_de..."
    for email in "hans.mueller@firma.de" "anna.schmidt@firma.de" "lukas.weber@unternehmen.eu"; do
        curl -s -o /dev/null -w "  %{http_code} $email\n" -X POST "$SUBSCRIBE_BASE/$DE_FORM_ID/subscribe" \
            -H "Content-Type: application/json" \
            -H "Origin: http://localhost:8070" \
            -d "{\"email\": \"$email\", \"consent\": \"true\"}"
    done
fi

echo "--------------------------------------"
echo "Operation complete."