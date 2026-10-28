#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP_DIR="${SCRIPT_DIR}"

# Target user directories
INSTALL_DIR="${HOME}/.local/opt/mycel"
DESKTOP_DIR="${HOME}/.local/share/applications"
ICONS_BASE_DIR="${HOME}/.local/share/icons/hicolor"

echo "Installing Mycel for user $(whoami)..."

# 1. Copy application files to ~/.local/opt/mycel
mkdir -p "${INSTALL_DIR}"
# Copy contents from published app directory (excluding packaging script directory itself)
if [ -d "${APP_DIR}/packaging" ]; then
    cp -r "${APP_DIR}"/* "${INSTALL_DIR}/"
else
    # We might be running inside the published root or packaging folder
    ROOT_PUBLISH="$(cd "${SCRIPT_DIR}/../.." && pwd)"
    if [ -f "${ROOT_PUBLISH}/Mycel" ]; then
        cp -r "${ROOT_PUBLISH}"/* "${INSTALL_DIR}/"
    else
        cp -r "${APP_DIR}"/* "${INSTALL_DIR}/"
    fi
fi
chmod +x "${INSTALL_DIR}/Mycel" || true

# 2. Install desktop entry with absolute Exec path
mkdir -p "${DESKTOP_DIR}"
DESKTOP_FILE="${DESKTOP_DIR}/mycel.desktop"
cat > "${DESKTOP_FILE}" << EOF
[Desktop Entry]
Type=Application
Name=Mycel
GenericName=Topology Optimisation
Comment=3D Topology Optimization for Additive Manufacturing
Exec=${INSTALL_DIR}/Mycel
Icon=mycel
Terminal=false
Categories=Graphics;Engineering;
StartupWMClass=Mycel
EOF
chmod 644 "${DESKTOP_FILE}"

# 3. Install hicolor icon assets
ICON_SIZES=(16 24 32 48 64 128 256 512)
for size in "${ICON_SIZES[@]}"; do
    TARGET_ICON_DIR="${ICONS_BASE_DIR}/${size}x${size}/apps"
    mkdir -p "${TARGET_ICON_DIR}"
    
    # Locate mycel_${size}.png in icons/ or Assets/icons/
    FOUND_ICON=""
    for candidate in \
        "${INSTALL_DIR}/icons/mycel_${size}.png" \
        "${INSTALL_DIR}/Assets/icons/mycel_${size}.png" \
        "${SCRIPT_DIR}/icons/mycel_${size}.png" \
        "${SCRIPT_DIR}/../../src/Mycel.App/Assets/icons/mycel_${size}.png"; do
        if [ -f "${candidate}" ]; then
            FOUND_ICON="${candidate}"
            break
        fi
    done

    if [ -n "${FOUND_ICON}" ]; then
        cp "${FOUND_ICON}" "${TARGET_ICON_DIR}/mycel.png"
        chmod 644 "${TARGET_ICON_DIR}/mycel.png"
    fi
done

# 4. Update desktop and icon databases if tools are available
if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database "${DESKTOP_DIR}" || true
fi

if command -v gtk-update-icon-cache >/dev/null 2>&1; then
    gtk-update-icon-cache -f -t "${ICONS_BASE_DIR}" || true
fi

echo "Mycel successfully installed to ${INSTALL_DIR}"
echo "Desktop entry created at ${DESKTOP_FILE}"

