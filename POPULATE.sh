#!/bin/bash

# Configuration
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
  "bedrijf.be"
  "korporacja.pl"
  "foretag.se"
)

# Permission Universe (10 total)
PERMISSION_KEYS=(
  "Marketing"
  "Services"
  "Newsletter"
  "Alerts"
  "Promotions"
  "Compliance"
  "Analytics"
  "Third Party Sharing"
  "SMS Notifications"
  "Telemarketing"
)

# Buckets
BUCKETS=(
  "marketing_campaign_dach_spring_2026"
  "marketing_campaign_nordics_sustainability_2026"
  "marketing_campaign_southern_europe_summer_2026"
  "product_update_eu_platform_q2_2026"
  "product_update_eu_platform_q3_2026"
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
  "webinar_series_q4_2026"
  "trade_show_leads_berlin"
  "ecommerce_cart_abandonment"
  "user_research_panel"
)

# Name pools keyed by TLD
get_first_names() {
    case "$1" in
        de|eu) echo "Hans Anna Lukas Emma Felix Laura Max Lena Thomas Julia" ;;
        fr)    echo "Jean Marie Pierre Sophie Thomas Emma Nicolas Julie Julien Claire" ;;
        it)    echo "Marco Giulia Luca Francesca Andrea Chiara Giovanni Elena Alessandro Sofia" ;;
        es)    echo "Carlos Laura Miguel Ana David Maria Jose Luis Antonio Sara" ;;
        nl|be) echo "Jan Sophie Lars Emma Thomas Sanne Pieter Anne Stefan Roos" ;;
        pl)    echo "Piotr Anna Krzysztof Katarzyna Tomasz Magdalena Marek Agnieszka Pawel Joanna" ;;
        se)    echo "Erik Anna Lars Emma Johan Sara Per Kristina Anders Maria" ;;
        *)     echo "Alex Jordan Morgan Casey Taylor Riley Cameron Quinn Avery Blake" ;;
    esac
}

get_last_names() {
    case "$1" in
        de|eu) echo "Muller Schmidt Weber Fischer Becker Wagner Meyer Schulz Hoffmann Koch" ;;
        fr)    echo "Dupont Martin Bernard Moreau Simon Laurent Lefebvre Leroy Roux Michel" ;;
        it)    echo "Rossi Ferrari Esposito Romano Colombo Ricci Marino Greco Bruno Conti" ;;
        es)    echo "Garcia Martinez Lopez Sanchez Perez Gonzalez Rodriguez Fernandez Diaz Torres" ;;
        nl|be) echo "Vries Bakker Janssen Visser Smit Meijer Boer Mulder Peters Dekker" ;;
        pl)    echo "Kowalski Nowak Wisniewski Wojcik Kowalczyk Kaminski Lewandowski Zielinski Szymanski Wozniak" ;;
        se)    echo "Andersson Johansson Karlsson Nilsson Eriksson Larsson Olsson Persson Svensson Gustafsson" ;;
        *)     echo "Smith Johnson Williams Brown Jones Garcia Miller Davis Wilson Moore" ;;
    esac
}

generate_name() {
    local tld="${1##*.}"
    read -ra firsts <<< "$(get_first_names "$tld")"
    read -ra lasts  <<< "$(get_last_names  "$tld")"
    echo "${firsts[$RANDOM % ${#firsts[@]}]} ${lasts[$RANDOM % ${#lasts[@]}]}"
}

# Functions
generate_email_permissions() {
    local bucket_perms=("$@")
    local total_perms=${#bucket_perms[@]}
    
    # Randomly decide how many of the available bucket permissions will be true for this user
    local true_count=$(( RANDOM % (total_perms + 1) ))

    if [[ $true_count -gt 0 ]]; then
        mapfile -t ENABLED < <(
            printf '%s\n' "${bucket_perms[@]}" \
            | shuf \
            | head -n "$true_count"
        )
    else
        ENABLED=()
    fi

    # Build JSON true/false map using only the bucket's assigned permissions
    local json=""
    for perm in "${bucket_perms[@]}"; do
        local is_enabled="false"
        for enabled_perm in "${ENABLED[@]}"; do
            if [[ "$enabled_perm" == "$perm" ]]; then
                is_enabled="true"
                break
            fi
        done
        json+="\"$perm\": $is_enabled,"
    done

    echo "${json%,}"
}


# Processing
echo "Initialization: Generating ${#BUCKETS[@]} buckets..."

for BUCKET_NAME in "${BUCKETS[@]}"; do

    NUM_EMAILS=$(( RANDOM % (MAX_EMAILS_PER_BUCKET - MIN_EMAILS_PER_BUCKET + 1) + MIN_EMAILS_PER_BUCKET ))
    
    # Randomly select between 3 and 7 permissions from the universe for this specific bucket
    NUM_BUCKET_PERMS=$(( RANDOM % 5 + 3 ))
    mapfile -t BUCKET_PERMISSIONS < <(
        printf '%s\n' "${PERMISSION_KEYS[@]}" \
        | shuf \
        | head -n "$NUM_BUCKET_PERMS"
    )

    echo "Processing [$BUCKET_NAME]: $NUM_EMAILS users with $NUM_BUCKET_PERMS available permissions..."

    for (( i=1; i<=NUM_EMAILS; i++ )); do
        USER_ID=$(LC_ALL=C tr -dc 'a-z0-9' < /dev/urandom | head -c 8)
        DOMAIN=${DOMAINS[$RANDOM % ${#DOMAINS[@]}]}
        EMAIL="${USER_ID}@${DOMAIN}"
        NAME=$(generate_name "$DOMAIN")

        PERMISSIONS_JSON=$(generate_email_permissions "${BUCKET_PERMISSIONS[@]}")

        PAYLOAD=$(cat <<EOF
[{
  "bucket": "$BUCKET_NAME",
  "email": "$EMAIL",
  "name": "$NAME",
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


# Submission Forms


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
        "disableRedirects": true,
        "collectName": true,
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
        "disableRedirects": true,
        "collectName": true,
        "formConfig": {
            "title": "Newsletter abonnieren",
            "description": "Erhalten Sie Updates direkt in Ihr Postfach.",
            "buttonText": "Abonnieren",
            "successMessage": "Vielen Dank für Ihre Anmeldung!"
        }
    }')

DE_FORM_ID=$(echo "$DE_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
echo "Created newsletter_de form: $DE_FORM_ID"

# Create newsletter_fr form
FR_RESPONSE=$(curl -s -X POST "$ADMIN_URL" \
    -H "X-Api-Key: $API_KEY" \
    -H "Content-Type: application/json" \
    -d '{
        "name": "French Newsletter",
        "bucket": "newsletter_fr",
        "permission": "Newsletter",
        "allowedOrigins": ["http://localhost:5000", "http://localhost:5001", "http://localhost:8070"],
        "language": "fr",
        "isEnabled": true,
        "disableRedirects": true,
        "collectName": true,
        "formConfig": {
            "title": "Abonnez-vous à la newsletter",
            "description": "Recevez des mises à jour dans votre boîte de réception.",
            "buttonText": "S'\''abonner",
            "successMessage": "Merci de vous être abonné !"
        }
    }')

FR_FORM_ID=$(echo "$FR_RESPONSE" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
echo "Created newsletter_fr form: $FR_FORM_ID"

# Subscribe 2 emails to newsletter_nl
if [[ -n "$NL_FORM_ID" ]]; then
    echo "Subscribing 2 emails to newsletter_nl..."
    declare -A NL_NAMES=(["jan.devries@organisatie.nl"]="Jan de Vries" ["sophie.bakker@organisatie.nl"]="Sophie Bakker")
    for email in "jan.devries@organisatie.nl" "sophie.bakker@organisatie.nl"; do
        curl -s -o /dev/null -w "  %{http_code} $email\n" -X POST "$SUBSCRIBE_BASE/$NL_FORM_ID/subscribe" \
            -H "Content-Type: application/json" \
            -H "Origin: http://localhost:8070" \
            -d "{\"email\": \"$email\", \"name\": \"${NL_NAMES[$email]}\", \"consent\": \"true\"}"
    done
fi

# Subscribe 3 emails to newsletter_de
if [[ -n "$DE_FORM_ID" ]]; then
    echo "Subscribing 3 emails to newsletter_de..."
    declare -A DE_NAMES=(["hans.mueller@firma.de"]="Hans Muller" ["anna.schmidt@firma.de"]="Anna Schmidt" ["lukas.weber@unternehmen.eu"]="Lukas Weber")
    for email in "hans.mueller@firma.de" "anna.schmidt@firma.de" "lukas.weber@unternehmen.eu"; do
        curl -s -o /dev/null -w "  %{http_code} $email\n" -X POST "$SUBSCRIBE_BASE/$DE_FORM_ID/subscribe" \
            -H "Content-Type: application/json" \
            -H "Origin: http://localhost:8070" \
            -d "{\"email\": \"$email\", \"name\": \"${DE_NAMES[$email]}\", \"consent\": \"true\"}"
    done
fi

# Subscribe 2 emails to newsletter_fr
if [[ -n "$FR_FORM_ID" ]]; then
    echo "Subscribing 2 emails to newsletter_fr..."
    declare -A FR_NAMES=(["jean.dupont@societe.fr"]="Jean Dupont" ["marie.curie@societe.fr"]="Marie Curie")
    for email in "jean.dupont@societe.fr" "marie.curie@societe.fr"; do
        curl -s -o /dev/null -w "  %{http_code} $email\n" -X POST "$SUBSCRIBE_BASE/$FR_FORM_ID/subscribe" \
            -H "Content-Type: application/json" \
            -H "Origin: http://localhost:8070" \
            -d "{\"email\": \"$email\", \"name\": \"${FR_NAMES[$email]}\", \"consent\": \"true\"}"
    done
fi

echo "--------------------------------------"
echo "Operation complete."