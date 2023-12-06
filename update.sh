#!/bin/bash
echo "Resetting the repository..."
git reset --hard
echo "Pulling from the repository..."
git pull
echo "Building bot..."
cd TrainBot
dotnet publish -c release
cd ..
echo "Running containers..."
docker-compose -f docker-compose.yml -f docker-compose.prod.yml up -d --force-recreate --build duck_api
docker-compose -f docker-compose.yml -f docker-compose.prod.yml up -d --force-recreate --build duck_bot
exit