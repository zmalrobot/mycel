#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "${SCRIPT_DIR}"

echo "========================================================================"
echo " Mycel Clean Build & Launch"
echo "========================================================================"

echo "--> Cleaning previous build outputs..."
dotnet clean -c Release --nologo

echo "--> Restoring dependencies..."
dotnet restore --nologo

echo "--> Building Mycel in Release mode..."
dotnet build -c Release --no-restore --nologo

echo "--> Launching Mycel.App..."
exec dotnet run --project src/Mycel.App/Mycel.App.csproj -c Release --no-build -- "$@"
