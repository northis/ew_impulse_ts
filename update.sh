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
docker compose up
exit