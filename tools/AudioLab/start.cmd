@echo off
setlocal
set "TASK_AUDIO_PYTHON=%USERPROFILE%\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe"
if exist "%TASK_AUDIO_PYTHON%" goto run
where python >nul 2>nul
if errorlevel 1 goto missing
set "TASK_AUDIO_PYTHON=python"
:run
"%TASK_AUDIO_PYTHON%" -X utf8 -u -i "%~dp0audio_lab.py"
exit /b %errorlevel%
:missing
echo Python 3.8+ x64 is required. Set TASK_AUDIO_PYTHON in this file to python.exe.
pause
exit /b 1
