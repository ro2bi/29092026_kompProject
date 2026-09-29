@echo off
cd /d "%~dp0"
dotnet build DribKrok.sln -m:1 --nologo
if errorlevel 1 exit /b 1
dotnet run --no-build --project DribKrok.Tests
