@echo off
setlocal

set COUNT=%1
if "%COUNT%"=="" set COUNT=1

set SERVER_URL=%2
if "%SERVER_URL%"=="" set SERVER_URL=http://localhost:5210

echo Starting %COUNT% clients...
echo Server: %SERVER_URL%

for /L %%i in (1,1,%COUNT%) do (
    echo Starting client %%i
    start "password-break-client-%%i" cmd /k dotnet run --project password-break-client\password-break-client.csproj -- %SERVER_URL%
)

echo Done.
endlocal