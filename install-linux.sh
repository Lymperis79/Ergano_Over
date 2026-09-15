#!/usr/bin/env bash
# install-linux.sh
# Installs ErganiManager to /opt/erganimanager and creates a desktop shortcut.
# Run as root or with sudo after running publish-linux.sh.

set -e

PUBLISH_DIR="publish/linux-x64"
INSTALL_DIR="/opt/erganimanager"
DESKTOP_FILE="/usr/share/applications/erganimanager.desktop"

if [ ! -f "$PUBLISH_DIR/ErganiManager.UI" ]; then
    echo "ERROR: $PUBLISH_DIR/ErganiManager.UI not found."
    echo "Run ./publish-linux.sh first, then re-run this installer."
    exit 1
fi

echo ""
echo "Installing ErganiManager to $INSTALL_DIR ..."
echo ""

# Install runtime dependencies
echo "Installing system dependencies ..."
if command -v apt-get &> /dev/null; then
    apt-get install -y libx11-6 libxrandr2 libxi6 libxcursor1 libfontconfig1 libice6 libsm6
elif command -v dnf &> /dev/null; then
    dnf install -y libX11 libXrandr libXi libXcursor fontconfig libICE libSM
elif command -v pacman &> /dev/null; then
    pacman -S --noconfirm libx11 libxrandr libxi libxcursor fontconfig
fi

# Copy files
mkdir -p "$INSTALL_DIR"
cp -r "$PUBLISH_DIR/." "$INSTALL_DIR/"
chmod +x "$INSTALL_DIR/ErganiManager.UI"

# Desktop shortcut
cp erganimanager.desktop "$DESKTOP_FILE"
chmod 644 "$DESKTOP_FILE"

# Update desktop DB
if command -v update-desktop-database &> /dev/null; then
    update-desktop-database /usr/share/applications/
fi

echo ""
echo "========================================"
echo "  ErganiManager installed successfully"
echo "========================================"
echo ""
echo "  Location:  $INSTALL_DIR/ErganiManager.UI"
echo "  Desktop:   $DESKTOP_FILE"
echo ""
echo "  To run from terminal:"
echo "    $INSTALL_DIR/ErganiManager.UI"
echo ""
