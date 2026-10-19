#!/bin/bash

# Resets the Beacon demo. Install: curl -fsSL https://raw.githubusercontent.com/melosso/beacon/main/.github/demo/pull.sh | bash. Cron: 0 * * * * cd /home/docker/beacon-demo && ./reset-demo.sh > reset.log 2>&1

REPO_RAW_URL="https://raw.githubusercontent.com/melosso/beacon/main/.github/demo"
ADMIN="http://localhost:5011"
API_KEY="beacon-demo"
ORIGIN="https://melosso.com"

fetch() {
    curl -fsSL "$REPO_RAW_URL/$1" -o "$1.tmp" && mv "$1.tmp" "$1" || { rm -f "$1.tmp"; echo "Failed to download $1"; return 1; }
}

cd "$(dirname "$0")" || exit 1

SELF="$(basename "$0")"
if [ -z "$BEACON_DEMO_SELF_UPDATED" ] && fetch "$SELF"; then
    chmod +x "$SELF"
    BEACON_DEMO_SELF_UPDATED=1 exec "./$SELF" "$@"
fi

fetch docker-compose.yml || [ -f docker-compose.yml ] || exit 1

if [ ! -f .env ]; then
    echo "Generating .env..."
    printf "BEACON_SIGNING_KEY=%s\nBEACON_ENCRYPTION_KEY=%s\nBEACON_PEPPER=%s\n" \
        "$(openssl rand -base64 32)" "$(openssl rand -base64 32)" "$(openssl rand -base64 32)" > .env
    chmod 600 .env
fi

echo "[$(date)] Recreating Beacon demo..."
docker compose down -v --remove-orphans
docker compose pull
docker compose up -d

for _ in $(seq 1 60); do
    curl -fs -o /dev/null "$ADMIN/health" && break
    sleep 2
done
curl -fs -o /dev/null "$ADMIN/health" || { echo "Beacon did not become healthy."; docker compose logs --tail 50; exit 1; }

post() {
    curl -fsS -X "${3:-POST}" "$ADMIN$1" -H "X-Api-Key: $API_KEY" -H "Content-Type: application/json" -d "$2"
}

echo "Configuring announcement bar and sign-in footer..."
post /api/admin/settings '{
    "promoBarEnabled": true,
    "promoBarShowOnLogin": true,
    "promoBarDismissable": false,
    "promoBar": "This is a demo instance. Some functions are disabled. All data is reset every hour.",
    "loginFooterEnabled": true,
    "loginFooter": "Sign in with API key **beacon-demo**. Read the [documentation](https://github.com/melosso/beacon) to learn more."
}' PUT >/dev/null || exit 1

# bucket|name|email|language|permissions|externalId
TOKENS=(
    'campaign_eu_q1|Iamgroot|iamgroot@me.com|en|"marketing":false,"system_updates":true|7c3e1e3a-6a6a-4c71-9e3e-8a6d8a9f4c21'
    'campaign_eu_q1|Beehive|beehive@acme.nl|nl|"marketing":true,"system_updates":true|3b9a9c6f-1c2e-4c52-9c45-9b4e2e8a1d77'
    'campaign_eu_q1|Alice|alice@le-pain.fr|fr|"marketing":true,"system_updates":true|a6f4b0c1-8c55-4f8a-8e6a-2d4d2d8f8f90'
    'campaign_eu_q1|Bob|bob@vag.de|de|"marketing":true,"system_updates":true|e2a9d8a4-4e61-4a6b-b2a3-6c5d9f1e3a44'
    'campaign_eu_q1|John Doe|john.doe@example.com|en|"marketing":true,"system_updates":true|11111111-1111-4111-8111-111111111111'
    'campaign_eu_q1|Jane Smith|jane.smith@example.com|en|"marketing":false,"system_updates":true|22222222-2222-4222-8222-222222222222'
    'campaign_eu_q1|Max Mustermann|max.mustermann@demo.de|de|"marketing":true,"system_updates":false|33333333-3333-4333-8333-333333333333'
    'campaign_eu_q1|Erika Mustermann|erika.mustermann@demo.de|de|"marketing":false,"system_updates":false|44444444-4444-4444-8444-444444444444'
    'campaign_eu_q1|Jean Dupont|jean.dupont@exemple.fr|fr|"marketing":true,"system_updates":true|55555555-5555-4555-8555-555555555555'
    'campaign_eu_q1|Marie Durand|marie.durand@exemple.fr|fr|"marketing":false,"system_updates":true|66666666-6666-4666-8666-666666666666'
    'campaign_eu_q1|Jan Jansen|jan.jansen@voorbeeld.nl|nl|"marketing":true,"system_updates":false|77777777-7777-4777-8777-777777777777'
    'campaign_eu_q1|Piet Devries|piet.devries@voorbeeld.nl|nl|"marketing":false,"system_updates":false|88888888-8888-4888-8888-888888888888'
    'campaign_eu_q1|Anna Kowalska|anna.kowalska@demo.pl|pl|"marketing":true,"system_updates":true|99999999-9999-4999-8999-999999999999'
    'campaign_eu_q1|Piotr Nowak|piotr.nowak@demo.pl|pl|"marketing":false,"system_updates":true|aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'
    'campaign_eu_q1|Carlos Garcia|carlos.garcia@empresa.es|es|"marketing":true,"system_updates":false|bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb'
    'campaign_eu_q1|Maria Lopez|maria.lopez@empresa.es|es|"marketing":false,"system_updates":false|cccccccc-cccc-4ccc-8ccc-cccccccccccc'
    'campaign_eu_q1|Tom Brown|tom.brown@example.com|en|"marketing":true,"system_updates":true|dddddddd-dddd-4ddd-8ddd-dddddddddddd'
    'campaign_eu_q1|Lisa White|lisa.white@example.com|en|"marketing":false,"system_updates":true|eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee'
    'campaign_eu_q1|Paul Mueller|paul.mueller@demo.de|de|"marketing":true,"system_updates":true|ffffffff-ffff-4fff-8fff-ffffffffffff'
    'campaign_eu_q1|Sophie Martin|sophie.martin@exemple.fr|fr|"marketing":false,"system_updates":false|12121212-1212-4121-8121-121212121212'
    'campaign_eu_q1|Kees Bakker|kees.bakker@voorbeeld.nl|nl|"marketing":true,"system_updates":false|34343434-3434-4343-8343-343434343434'
    'campaign_eu_q1|Tomasz Zielinski|tomasz.zielinski@demo.pl|pl|"marketing":true,"system_updates":false|56565656-5656-4565-8565-565656565656'
    'campaign_eu_q1|Lucia Martinez|lucia.martinez@empresa.es|es|"marketing":false,"system_updates":true|78787878-7878-4787-8787-787878787878'
    'campaign_eu_q2|Carlos|carlos@empresa.es|es|"weekly_updates":true,"monthly_updates":false,"annual_updates":true|9b7d3a1e-1f4a-4f3c-b9b5-7d2b8e6a1c11'
    'campaign_eu_q2|Anna|anna@firma.de|de|"weekly_updates":false,"monthly_updates":true,"annual_updates":false|c2a6f8b1-3a44-4a8e-8f7c-0a2b9d4e6f22'
    'campaign_eu_q2|Marie|marie@societe.fr|fr|"weekly_updates":true,"monthly_updates":true,"annual_updates":false|4f9a1d2c-7b55-4d8f-ae1b-6c3f8a9e3d33'
    'campaign_eu_q2|Jan|jan@bedrijf.nl|nl|"weekly_updates":false,"monthly_updates":false,"annual_updates":true|d8e3b2a9-5c66-4c1b-9f7e-1a4d6b8c4444'
    'campaign_eu_q2|Ewa|ewa@firma.pl|pl|"weekly_updates":true,"monthly_updates":false,"annual_updates":false|a3c4e9b7-8d77-4a2c-b1e9-5f6a2d8c5555'
    'campaign_eu_q2|Pieter|pieter@company.nl|nl|"weekly_updates":false,"monthly_updates":true,"annual_updates":true|6b8d1f3a-9e88-4b5c-8a2f-7c4e5d9a6666'
    'campaign_eu_q2|Laura|laura@company.de|de|"weekly_updates":true,"monthly_updates":true,"annual_updates":true|f1a9b2c3-0a99-4f6d-9b7e-8d5c3a2b7777'
    'campaign_eu_q2|Miguel|miguel@empresa.es|es|"weekly_updates":false,"monthly_updates":false,"annual_updates":false|3c7a5b8d-1faa-4d8a-b9c2-6e4f1d0a8888'
    'system_updates|Systems|systems@heyyou.com|en|"weekly_updates":true,"monthly_updates":false,"annual_updates":true|9b7d3a1e-1f4a-4f3c-b9b5-7d2b8e6a1c11'
    'system_updates|Administrator|administrator@harz.co.uk|en|"weekly_updates":false,"monthly_updates":true,"annual_updates":false|4f9a1d2c-3a44-4a8e-8f7c-6e4f1d0a8888'
)

echo "Generating ${#TOKENS[@]} preference tokens..."
BODY=""
for row in "${TOKENS[@]}"; do
    IFS='|' read -r bucket name email lang perms ext <<< "$row"
    BODY+="${BODY:+,}{\"bucket\":\"$bucket\",\"name\":\"$name\",\"email\":\"$email\",\"language\":\"$lang\",\"permissions\":{$perms},\"allowReplay\":true,\"expiryDays\":1,\"skipPermissionUpdate\":true,\"customFields\":{\"externalId\":\"$ext\",\"source\":\"demo\"}}"
done
post /api/tokens/generate "[$BODY]" >/dev/null || exit 1

# form <bucket> <name> <language> <title> <description> <button> <success> <contacts: "Name:email" ...>
form() {
    local bucket=$1 name=$2 lang=$3 title=$4 desc=$5 button=$6 success=$7
    shift 7
    local id
    id=$(post /api/admin/submissions "{
        \"name\": \"$name\", \"bucket\": \"$bucket\", \"permission\": \"Newsletter\", \"collectName\": true,
        \"allowedOrigins\": [\"https://beacon-demo-api.melosso.com\", \"https://beacon-demo.melosso.com\", \"$ORIGIN\"],
        \"language\": \"$lang\", \"isEnabled\": true, \"disableRedirects\": true,
        \"formConfig\": {\"title\": \"$title\", \"description\": \"$desc\", \"buttonText\": \"$button\", \"successMessage\": \"$success\"},
        \"customFields\": {\"uniqueId\": \"{{uuid}}\", \"timestamp\": \"{{date:Europe/Amsterdam}}\", \"formName\": \"{{form_name}}\", \"formId\": \"{{form_id}}\", \"source\": \"{{source}}\"}
    }" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
    [ -n "$id" ] || { echo "Form $bucket creation failed."; return 1; }
    echo "Created form $bucket: $id"
    for contact in "$@"; do
        curl -s -o /dev/null -w "  %{http_code} ${contact##*:}\n" -X POST "$ADMIN/api/submission/$id/subscribe" \
            -H "Content-Type: application/json" -H "Origin: $ORIGIN" \
            -d "{\"name\": \"${contact%%:*}\", \"email\": \"${contact##*:}\", \"consent\": \"true\"}"
    done
}

form newsletter_de "German Newsletter" de "Newsletter abonnieren" "Erhalten Sie Updates direkt in Ihr Postfach." "Abonnieren" "Vielen Dank für Ihre Anmeldung!" \
    "Hans Mueller:hans.mueller@firma.de" "Anna Schmidt:anna.schmidt@firma.de" "Lukas Weber:lukas.weber@unternehmen.eu" || exit 1
form newsletter_en "English Newsletter" en "Subscribe to our newsletter" "Receive updates directly in your inbox." "Subscribe" "Thank you for subscribing!" \
    "John Smith:john.smith@company.co.uk" "Emily Jones:emily.jones@business.com" "Oliver Brown:oliver.brown@enterprise.eu" "Sophia Taylor:sophia.taylor@firm.org" || exit 1

echo "[$(date)] Demo reset successfully!"
