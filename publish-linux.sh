#!/usr/bin/env bash
set -e

PROJECT="src/ErganiManager.UI/ErganiManager.UI.csproj"
OUTPUT="publish/linux-x64"

echo ""
echo "========================================"
echo "  ErganiManager — Linux x64 Publish"
echo "========================================"
echo ""

dotnet publish "$PROJECT" \
    --configuration Release \
    --runtime linux-x64 \
    --self-contained true \
    --output "$OUTPUT" \
    /p:PublishSingleFile=true \
    /p:IncludeNativeLibrariesForSelfExtract=true \
    /p:EnableCompressionInSingleFile=true \
    /p:DebugType=embedded

chmod +x "$OUTPUT/ErganiManager.UI"

echo ""
echo "========================================"
echo "  Output: $(pwd)/$OUTPUT"
echo "========================================"
echo ""
echo "  Executable: $OUTPUT/ErganiManager.UI"
echo ""
echo "  Linux dependencies needed on target machine:"
echo "    sudo apt install libx11-6 libxrandr2 libxi6 libxcursor1 libfontconfig1"
echo "  (Avalonia requires these X11/Wayland libs — they are NOT bundled)"
echo ""
