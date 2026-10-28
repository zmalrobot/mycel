#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "${SCRIPT_DIR}"

VERSION="1.0.0"
if [ -f "Directory.Build.props" ]; then
    DETECTED_VERSION=$(grep -oPm1 "(?<=<Version>)[^<]+" Directory.Build.props || true)
    if [ -n "${DETECTED_VERSION}" ]; then
        VERSION="${DETECTED_VERSION}"
    fi
fi

echo "========================================================================"
echo " Building Mycel v${VERSION} Release Packages"
echo "========================================================================"

mkdir -p dist
mkdir -p artifacts/linux-x64
mkdir -p artifacts/win-x64

echo ""
echo "--> Step 1/4: Running automated tests..."
dotnet test -c Release --filter "FullyQualifiedName!~PerformanceProfilingTests" --nologo

echo ""
echo "--> Step 2/4: Publishing linux-x64 (self-contained, SingleFile, ReadyToRun)..."
dotnet publish src/Mycel.App/Mycel.App.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained \
    -p:PublishSingleFile=true \
    -p:PublishReadyToRun=true \
    -p:EnableCompressionInSingleFile=true \
    -o artifacts/linux-x64 \
    --nologo

cp LICENSE artifacts/linux-x64/
cp THIRD_PARTY_NOTICES.md artifacts/linux-x64/
mkdir -p artifacts/linux-x64/packaging/linux
cp -r packaging/linux/* artifacts/linux-x64/packaging/linux/
mkdir -p artifacts/linux-x64/icons
cp src/Mycel.App/Assets/icons/*.png artifacts/linux-x64/icons/

echo "Creating dist/Mycel-${VERSION}-linux-x64.tar.gz..."
tar -czf "dist/Mycel-${VERSION}-linux-x64.tar.gz" -C artifacts/linux-x64 .

echo ""
echo "--> Step 3/4: Publishing win-x64 (self-contained, SingleFile, ReadyToRun)..."
dotnet publish src/Mycel.App/Mycel.App.csproj \
    -c Release \
    -r win-x64 \
    --self-contained \
    -p:PublishSingleFile=true \
    -p:PublishReadyToRun=true \
    -p:EnableCompressionInSingleFile=true \
    -o artifacts/win-x64 \
    --nologo

cp LICENSE artifacts/win-x64/
cp THIRD_PARTY_NOTICES.md artifacts/win-x64/

echo "Creating dist/Mycel-${VERSION}-win-x64.zip..."
(cd artifacts/win-x64 && zip -q -r "../../dist/Mycel-${VERSION}-win-x64.zip" .)

echo ""
echo "--> Step 4/4: Verification of release packages..."
ls -lh dist/Mycel-${VERSION}-linux-x64.tar.gz
ls -lh dist/Mycel-${VERSION}-win-x64.zip

echo ""
echo "========================================================================"
echo " SUCCESS: Mycel v${VERSION} packages generated in dist/"
echo "========================================================================"

