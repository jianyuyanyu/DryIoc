@echo off
setlocal EnableDelayedExpansion

rem Calculate start time
set started_at=%time%
set /a started_at_ms=1%started_at:~0,2%*24*60*100-100+%started_at:~3,2%*60*100+%started_at:~6,2%*100+%started_at:~9,2%

echo:
echo:# Build and Run TestRunners for Latest supported .NET and .NET FRAMEWORK 4.7.2
echo:[started at %started_at%]
echo:
echo:## Latest supported .NET
dotnet run -v:m -f:net9.0 -c:Release -p:UseCompilationOnly=true --project test/DryIoc.TestRunner/DryIoc.TestRunner.csproj
echo:
echo:## .NET FRAMEWORK 4.7.2
dotnet run -v:m -c:Release -p:UseCompilationOnly=true --project test/DryIoc.TestRunner.net472/DryIoc.TestRunner.net472.csproj

if %ERRORLEVEL% neq 0 goto :error

rem Calculate elapsed time
set finished_at=%time%
set /a finished_at_ms=1%finished_at:~0,2%*24*60*100-100+%finished_at:~3,2%*60*100+%finished_at:~6,2%*100+%finished_at:~9,2%
set /a ellapsed_ms=%finished_at_ms%*10-%started_at_ms%*10

echo:
echo:[finished at %finished_at%, elapsed: %ellapsed_ms% ms]
echo:# Finished: ALL Successful
exit /b 0

:error
echo:
echo:# Finished: Something failed :-(
exit /b 1
