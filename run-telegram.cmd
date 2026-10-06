@echo off
cd /d "%~dp0"
dotnet run --project DribKrok.Telegram -c Release
if errorlevel 1 pause
