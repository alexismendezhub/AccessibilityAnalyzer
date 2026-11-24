@echo off
REM ============================================================
REM  AccessibilityAnalyzer - builds and runs ALL tests on Windows
REM  Requirements: .NET SDK 10, Node.js 22.22.3+/24.15+, trusted ASP.NET Core dev certificate (smoke test)
REM  Usage: scripts\build-and-test.cmd   (from the repo root)
REM ============================================================
setlocal
cd /d "%~dp0.."

echo.
echo === 1/5 Restore and build the solution (.slnx), warnings as errors ===
dotnet build AccessibilityAnalyzer.slnx -c Release || goto :error

echo.
echo === 2/5 .NET tests (xUnit) ===
dotnet test AccessibilityAnalyzer.slnx -c Release --no-build || goto :error

echo.
echo === 3/5 Angular dependencies and tests (Node test runner) ===
pushd src\accessibilityanalyzer.client
call npm install || (popd & goto :error)
call npm test || (popd & goto :error)

echo.
echo === 4/5 Angular production build ===
call npm run build || (popd & goto :error)
popd

echo.
echo === 5/5 Real-server smoke test (publish + Kestrel over HTTPS) ===
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\smoke-test.ps1 || goto :error

echo.
echo ===== ALL OK =====
exit /b 0

:error
echo.
echo ***** FAILED (code %errorlevel%) *****
exit /b 1
