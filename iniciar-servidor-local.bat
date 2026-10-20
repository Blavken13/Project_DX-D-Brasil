@echo off
setlocal EnableExtensions DisableDelayedExpansion
title Durango Brasil - Servidor local

rem Usa caminhos relativos ao BAT, mesmo quando aberto de outra pasta.
set "PROJECT_ROOT=%~dp0"
set "SERVER_DIR=%PROJECT_ROOT%Durango-CustomServer\server"
set "SERVER_PROJECT=%SERVER_DIR%\DurangoServer.csproj"
set "LAUNCHER=%PROJECT_ROOT%tools\iniciar-servidor-local.ps1"

where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERRO] O SDK do .NET 9 nao foi encontrado no PATH.
    goto :failure
)
if not exist "%SERVER_PROJECT%" (
    echo [ERRO] Projeto do servidor nao encontrado: "%SERVER_PROJECT%".
    goto :failure
)
if not exist "%LAUNCHER%" (
    echo [ERRO] Inicializador nao encontrado: "%LAUNCHER%".
    goto :failure
)
if not exist "%SERVER_DIR%\data\assets\constants.json" (
    echo [ERRO] Dados do jogo nao encontrados em "%SERVER_DIR%\data".
    goto :failure
)
if not exist "%SERVER_DIR%\data\terrains\*.zip" (
    echo [ERRO] Mapas nao encontrados em "%SERVER_DIR%\data\terrains".
    goto :failure
)

pushd "%PROJECT_ROOT%"
if errorlevel 1 goto :failure
powershell.exe -NoLogo -NoProfile -File "%LAUNCHER%" -CheckAvailable
if errorlevel 1 goto :preflight_failure
echo.
echo Compilando a versao atual do servidor...
dotnet build "%SERVER_PROJECT%" -c Debug --nologo --verbosity minimal
if errorlevel 1 goto :build_failure

rem Valida tambem se o Windows ou o WSL consegue executar a DLL compilada.
if /i "%~1"=="--check" (
    powershell.exe -NoLogo -NoProfile -File "%LAUNCHER%" -Check
    goto :check_finished
)
powershell.exe -NoLogo -NoProfile -File "%LAUNCHER%"
set "SERVER_EXIT=%ERRORLEVEL%"
popd
echo.
echo Servidor encerrado. Codigo: %SERVER_EXIT%
pause
exit /b %SERVER_EXIT%

:check_finished
set "SERVER_EXIT=%ERRORLEVEL%"
popd
exit /b %SERVER_EXIT%

:build_failure
popd
echo [ERRO] A compilacao falhou. O servidor nao foi iniciado.
goto :failure
:preflight_failure
popd
:failure
if /i not "%~1"=="--check" pause
exit /b 1
