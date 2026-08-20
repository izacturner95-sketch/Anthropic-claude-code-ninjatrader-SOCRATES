@echo off
REM Double-click to check the SocratesData files for gaps.
python "%~dp0check_file_series.py" %*
if errorlevel 1 pause
