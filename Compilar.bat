@echo off
cd /d "%~dp0"
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o ..\MiniPlayer-Portatil
if errorlevel 1 (echo. & echo Instale o .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0 & pause & exit /b)
start "" "..\MiniPlayer-Portatil\MiniPlayer.exe"
