@echo off
cd /d "%~dp0"
dotnet build DribKrok.Web\DribKrok.Web.csproj -m:1 --nologo
if errorlevel 1 goto failed
echo Open http://127.0.0.1:5186 in your browser. Press Ctrl+C to stop.
dotnet run --no-build --project DribKrok.Web
goto end
:failed
echo Build failed. Check that .NET SDK 8 or 9 is installed.
:end
pause
