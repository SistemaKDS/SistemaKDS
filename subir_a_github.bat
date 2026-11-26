@echo off
chcp 65001 >nul
cd /d "%~dp0"
where git >/dev/null 2>/dev/null || (echo No encontre Git. Instalalo desde https://git-scm.com/download/win y vuelve a abrir este archivo. & pause & exit /b 1)
set "CARPETA=%TEMP%\kds_subir_%RANDOM%%RANDOM%"
git clone -q -b actualizacion-pagos-bot-avisos "%~dp0subir_a_github.bundle" "%CARPETA%" || (echo No pude abrir el paquete. & pause & exit /b 1)
cd /d "%CARPETA%"
git push https://github.com/SistemaKDS/SistemaKDS.git actualizacion-pagos-bot-avisos
if errorlevel 1 (echo. & echo No se pudo subir. Revisa que iniciaste sesion en GitHub con la cuenta de SistemaKDS. & pause & exit /b 1)
echo.
echo Listo: la rama actualizacion-pagos-bot-avisos ya esta en GitHub. Avisale a Claude.
pause
