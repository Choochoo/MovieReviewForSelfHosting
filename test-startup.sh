#!/bin/bash
cd /mnt/c/Users/Jared/source/repos/Choochoo/MovieReviewApp/MovieReviewApp
echo "Testing MovieReviewApp startup with WSL path fix..."
echo "Current directory: $(pwd)"
echo "Checking for dotnet command..."

# Try to find dotnet in common Windows locations when running in WSL
DOTNET_PATHS=(
    "/mnt/c/Program Files/dotnet/dotnet.exe"
    "/mnt/c/Program Files (x86)/dotnet/dotnet.exe"
    "/usr/bin/dotnet"
    "dotnet"
)

DOTNET_CMD=""
for path in "${DOTNET_PATHS[@]}"; do
    if command -v "$path" &> /dev/null || [ -f "$path" ]; then
        DOTNET_CMD="$path"
        echo "Found dotnet at: $DOTNET_CMD"
        break
    fi
done

if [ -z "$DOTNET_CMD" ]; then
    echo "ERROR: dotnet command not found. Please install .NET SDK."
    exit 1
fi

echo "Starting application..."
"$DOTNET_CMD" run --no-build 2>&1 | head -50