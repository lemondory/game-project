@echo off
REM GameServer Data Generation Script (Windows)
REM Generates C# classes and MessagePack binary files from XLSX data

setlocal enabledelayedexpansion

echo === GameServer Data Generator ===
echo.

REM Directories
set "SCRIPT_DIR=%~dp0"
set "PROJECT_ROOT=%SCRIPT_DIR%.."
set "CONVERTER_PROJECT=%SCRIPT_DIR%DataConverter\DataConverter\DataConverter.csproj"

set "INPUT_DIR=%PROJECT_ROOT%\data\xlsx"
set "BYTES_DIR=%PROJECT_ROOT%\data\bytes"
set "CSV_DIR=%PROJECT_ROOT%\data\csv"

REM Output directories
set "SERVER_CODE_DIR=%PROJECT_ROOT%\src\GameShared\Generated\Data"
set "SERVER_ENUM_DIR=%PROJECT_ROOT%\src\GameShared\Generated\Enums"

REM Unity client path (if exists)
set "UNITY_CODE_DIR=..\UnityClient\Assets\Scripts\Generated\Data"
set "UNITY_ENUM_DIR=..\UnityClient\Assets\Scripts\Generated\Enums"

REM Check if input directory exists
if not exist "%INPUT_DIR%" (
    echo ❌ Error: Input directory not found: %INPUT_DIR%
    echo    Please create XLSX files in data\xlsx\
    exit /b 1
)

REM Check if enums.xlsx exists
if not exist "%INPUT_DIR%\enums.xlsx" (
    echo ⚠️  Warning: enums.xlsx not found. Enums will not be generated.
)

REM Run DataConverter
echo 🔄 Running DataConverter...
echo.

dotnet run --project "%CONVERTER_PROJECT%" -- ^
    --input "%INPUT_DIR%" ^
    --output-code "%SERVER_CODE_DIR%" ^
    --output-bytes "%BYTES_DIR%" ^
    --output-csv "%CSV_DIR%" ^
    --enums "%INPUT_DIR%\enums.xlsx"

if errorlevel 1 (
    echo.
    echo ❌ DataConverter failed
    exit /b 1
)

REM Check if Unity client directory exists
if exist "%UNITY_CODE_DIR%\.." (
    echo.
    echo 📦 Copying to Unity client...

    if not exist "%UNITY_CODE_DIR%" mkdir "%UNITY_CODE_DIR%"
    if not exist "%UNITY_ENUM_DIR%" mkdir "%UNITY_ENUM_DIR%"
    if not exist "%UNITY_CODE_DIR%\..\Resources\Data" mkdir "%UNITY_CODE_DIR%\..\Resources\Data"

    REM Copy generated C# files
    xcopy /Y /E /I "%SERVER_CODE_DIR%\*" "%UNITY_CODE_DIR%\"
    xcopy /Y /E /I "%SERVER_ENUM_DIR%\*" "%UNITY_ENUM_DIR%\"

    REM Copy .bytes files to Unity Resources
    xcopy /Y /E /I "%BYTES_DIR%\*" "%UNITY_CODE_DIR%\..\Resources\Data\"

    echo ✓ Unity client files updated
)

echo.
echo === Generation Complete ===
echo.
echo Generated files:
echo   📄 C# Classes: %SERVER_CODE_DIR%
echo   📄 C# Enums:   %SERVER_ENUM_DIR%
echo   📦 Bytes:      %BYTES_DIR%
echo   📊 CSV:        %CSV_DIR%

if exist "%UNITY_CODE_DIR%\.." (
    echo   🎮 Unity:      %UNITY_CODE_DIR%
)

echo.
pause
