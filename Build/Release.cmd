@echo off
@setlocal
@cd /d "%~dp0"

@set "PWSH="
@where pwsh.exe >nul 2>nul
@if not errorlevel 1 @set "PWSH=pwsh.exe"

@if not defined PWSH @if exist "C:\Program Files\PowerShell\7\pwsh.exe" @set "PWSH=C:\Program Files\PowerShell\7\pwsh.exe"

@if not defined PWSH (
    @echo PowerShell 7 ^(pwsh.exe^) wurde nicht gefunden.
    @echo Geprueft wurden PATH und C:\Program Files\PowerShell\7\pwsh.exe
    @pause
    @exit /b 1
)

@"%PWSH%" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Increment-Version.ps1"
@set "ERR=%ERRORLEVEL%"

@if not "%ERR%"=="0" (
    @echo.
    @echo VERSIONIERUNG FEHLGESCHLAGEN - Fehlercode %ERR%
    @pause
    @exit /b %ERR%
)

@"%PWSH%" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Release.ps1"
@set "ERR=%ERRORLEVEL%"

@if not "%ERR%"=="0" (
    @echo.
    @echo BUILD FEHLGESCHLAGEN - Fehlercode %ERR%
    @pause
    @exit /b %ERR%
)

@echo.
@echo BUILD ERFOLGREICH
@pause
@exit /b 0
