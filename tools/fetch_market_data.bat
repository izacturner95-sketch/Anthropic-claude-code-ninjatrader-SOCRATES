@echo off
rem Double-click launcher for fetch_market_data.py.
rem
rem Exists because double-clicking a .py file on Windows runs it in a console that
rem closes the instant the process ends - so every message it prints, including the
rem one saying what went wrong, is a flash. This holds the window open and finds
rem Python whichever way it was installed.

setlocal

set "TARGET=%USERPROFILE%\Documents\NinjaTrader 8\SocratesData"

rem The py launcher ships with the python.org installer; python.exe is what the
rem Microsoft Store build and most PATH setups provide. Try both before giving up.
where py >nul 2>&1
if %errorlevel%==0 (
    py "%~dp0fetch_market_data.py" --out "%TARGET%" %*
    goto done
)

where python >nul 2>&1
if %errorlevel%==0 (
    python "%~dp0fetch_market_data.py" --out "%TARGET%" %*
    goto done
)

echo.
echo Python was not found.
echo.
echo Install it from https://www.python.org/downloads/ and tick
echo "Add Python to PATH" during setup, then run this again.

:done
echo.
pause
