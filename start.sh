#!/bin/bash
if [[ $EUID -ne 0 ]]; then
   echo "This script must be run as root" 
   exit 1
fi

if ! [ -x "$(command -v docker-compose)" ]; then
    echo "Please, install docker compose"
   exit 1
fi

# Load up .env
echo "Loading from your .env file..."
set -o allexport
[[ -f .env ]] && source .env
set +o allexport

mkdir ssl

cd config
echo "Applying the settings..."

#replace appsettings.json
sed -r -e "s/webpartsite.com/$PUBLIC_URL/; s/TELEGRAM_BOT_KEY/$TELEGRAM_BOT_KEY/; \
s/PWD/$SERVICE_COMMAND/; s/(\"AdminUserId\":\s)([0-9]+)/\1$ADMIN_USER_ID/g; \
s/(\"ServerUserId\":\s)([0-9]+)/\1$SERVER_USER_ID/g; s/(\"UserId\":\s)([0-9]+)/\1$USER_ID/g" appsettings.template.json > appsettings.json

#replace websrv.conf
sed -e "s/SERVER_NAME/$PUBLIC_URL/; s/TELEGRAM_BOT_KEY/$TELEGRAM_BOT_KEY/" \
websrv.template.conf > websrv.conf

cd ..
#replace docker-compose.prod.yml
sed -e "s/CERT_EMAIL/$CERT_EMAIL/; s/PUBLIC_URL/$PUBLIC_URL/" docker-compose.yml.template > docker-compose.yml

echo "Building bot..."
cd TrainBot
dotnet publish -c release /p:NoBuild=false
cd ..
cp ./config/appsettings.json ./TrainBot/bin/release/net7.0/publish/appsettings.json

echo "Running containers..."
docker compose up

echo "Reloading proxy nginx server..."
sleep .10
docker exec train_web nginx -s reload
exit

