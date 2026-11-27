@echo off
rem Lupin Farm build: Windows built-in .NET Framework 4 compiler (C# 5). No install needed.
rem   build.bat        -> bin\lupin.exe
rem   build.bat test   -> bin\tests.exe and run it (bin\tests.exe balance = balance report)
setlocal
cd /d "%~dp0"

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo [ERROR] csc.exe not found
  exit /b 1
)
if not exist bin mkdir bin

set FLAGS=/nologo /codepage:65001 /target:exe /optimize+ /warn:4 /r:System.dll /r:System.Core.dll /r:System.Web.Extensions.dll

if /i "%~1"=="test" goto test

"%CSC%" %FLAGS% /out:bin\lupin.exe /recurse:src\*.cs
if errorlevel 1 (
  echo [ERROR] build failed
  exit /b 1
)
echo [OK] bin\lupin.exe
exit /b 0

:test
"%CSC%" %FLAGS% /out:bin\tests.exe /recurse:src\Core\*.cs /recurse:src\Data\*.cs /recurse:src\Storage\*.cs /recurse:src\Ui\*.cs /recurse:tests\*.cs
if errorlevel 1 (
  echo [ERROR] test build failed
  exit /b 1
)
bin\tests.exe
exit /b %errorlevel%
