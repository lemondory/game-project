#!/bin/bash

# GameServer Data Generation Script
# Generates C# classes and MessagePack binary files from XLSX data

set -e  # Exit on error

echo "=== GameServer Data Generator ==="
echo ""

# Directories
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
CONVERTER_PROJECT="$SCRIPT_DIR/DataConverter/DataConverter/DataConverter.csproj"

INPUT_DIR="$PROJECT_ROOT/data/xlsx"
BYTES_DIR="$PROJECT_ROOT/data/bytes"
CSV_DIR="$PROJECT_ROOT/data/csv"

# Output directories
SERVER_CODE_DIR="$PROJECT_ROOT/src/GameShared/Generated/Data"
SERVER_ENUM_DIR="$PROJECT_ROOT/src/GameShared/Generated/Enums"

# Unity client path (if exists)
UNITY_CODE_DIR="../UnityClient/Assets/Scripts/Generated/Data"
UNITY_ENUM_DIR="../UnityClient/Assets/Scripts/Generated/Enums"

# Check if input directory exists
if [ ! -d "$INPUT_DIR" ]; then
    echo "❌ Error: Input directory not found: $INPUT_DIR"
    echo "   Please create XLSX files in data/xlsx/"
    exit 1
fi

# Check if enums.xlsx exists
if [ ! -f "$INPUT_DIR/enums.xlsx" ]; then
    echo "⚠️  Warning: enums.xlsx not found. Enums will not be generated."
fi

# Run DataConverter
echo "🔄 Running DataConverter..."
echo ""

dotnet run --project "$CONVERTER_PROJECT" -- \
    --input "$INPUT_DIR" \
    --output-code "$SERVER_CODE_DIR" \
    --output-bytes "$BYTES_DIR" \
    --output-csv "$CSV_DIR" \
    --enums "$INPUT_DIR/enums.xlsx"

# Check if Unity client directory exists
if [ -d "$(dirname "$UNITY_CODE_DIR")" ]; then
    echo ""
    echo "📦 Copying to Unity client..."

    mkdir -p "$UNITY_CODE_DIR"
    mkdir -p "$UNITY_ENUM_DIR"
    mkdir -p "$(dirname "$UNITY_CODE_DIR")/Resources/Data"

    # Copy generated C# files
    cp -r "$SERVER_CODE_DIR"/* "$UNITY_CODE_DIR/"
    cp -r "$SERVER_ENUM_DIR"/* "$UNITY_ENUM_DIR/"

    # Copy .bytes files to Unity Resources
    cp -r "$BYTES_DIR"/* "$(dirname "$UNITY_CODE_DIR")/Resources/Data/"

    echo "✓ Unity client files updated"
fi

echo ""
echo "=== Generation Complete ==="
echo ""
echo "Generated files:"
echo "  📄 C# Classes: $SERVER_CODE_DIR"
echo "  📄 C# Enums:   $SERVER_ENUM_DIR"
echo "  📦 Bytes:      $BYTES_DIR"
echo "  📊 CSV:        $CSV_DIR"

if [ -d "$(dirname "$UNITY_CODE_DIR")" ]; then
    echo "  🎮 Unity:      $UNITY_CODE_DIR"
fi

echo ""
