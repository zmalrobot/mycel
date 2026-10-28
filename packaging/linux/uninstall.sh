#!/usr/bin/env bash
set -euo pipefail

INSTALL_DIR="${HOME}/.local/opt/mycel"
DESKTOP_FILE="${HOME}/.local/share/applications/mycel.desktop"
ICONS_BASE_DIR="${HOME}/.local/share/icons/hicolor"

echo "Uninstalling Mycel for user $(whoami)..."

# 1. Remove installed binaries and files
if [ -d "${INSTALL_DIR}" ]; then
    rm -rf "${INSTALL_DIR}"
    echo "Removed ${INSTALL_DIR}"
fi

# 2. Remove desktop shortcut
if [ -f "${DESKTOP_FILE}" ]; then
    rm -f "${DESKTOP_FILE}"
    echo "Removed ${DESKTOP_FILE}"
fi

# 3. Remove installed icon assets
ICON_SIZES=(16 24 32 48 64 128 256 512)
for size in "${ICON_SIZES[@]}"; do
    ICON_PATH="${ICONS_BASE_DIR}/${size}x${size}/apps/mycel.png"
    if [ -f "${ICON_PATH}" ]; then
        rm -f "${ICON_PATH}"
    fi
done

# 4. Refresh caches
if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database "${HOME}/.local/share/applications" || true
fi

if command -v gtk-update-icon-cache >/dev/null 2>&1; then
    gtk-update-icon-cache -f -t "${ICONS_BASE_DIR}" || true
fi

echo "Mycel has been uninstalled."

