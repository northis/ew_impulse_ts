#!/bin/bash
echo "Resetting the repository..."
git reset --hard
echo "Pulling from the repository..."
git pull
echo "Building bot..."
cd TrainBot
dotnet publish -c release /p:NoBuild=false
cd ..
echo "Running containers..."
docker compose restart train_bot
exit