@echo off

dotnet workload search

echo.
echo Above to see what's will be installed.
echo.
pause

for /f "tokens=1" %%i in ('dotnet workload search ^| tail -n +4') do (
  echo.
  echo Installing %%i...
  dotnet workload install %%i
)

timeout /nobreak -1
