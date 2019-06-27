@echo off
SETLOCAL
@echo --- MakeVersion::begin ------------------------------------------------

rem UTF8 (no diff) VS2010 (Win7) can't handle
rem chcp 65001
SET CURDIR=%~dp0
CD %CURDIR%
rem @echo cur dir: '%CURDIR%'
rem @@@ example how to retrieve svn revision from current folder
SET RELEASE_REVISION=0
for /f "delims=: tokens=1,2" %%a in ('svn info') do (
  if "%%a"=="Revision" (
    set /a RELEASE_REVISION=%%b
    rem set /A RELEASE_REVISION=RELEASE_REVISION+1
  )
)

REM @@@ clear errorlevel from previous svn command or if %errorlevel% NEQ "0" exit).
REM @@@ depends on logic you need, I am presenting much less known clear solution
ver > nul

REM @@@ just add new blank line
rem @echo.

SET MV1=%CD%\makeversion.exe
SET MV2=%CD%\..\makeversion.exe
SET MV3="%CD%\..\debug\makeversion.exe"
SET MV=
IF NOT EXIST "%MV1%" (   
  IF NOT EXIST "%MV2%" ( 
    IF NOT EXIST "%MV3%" ( 
      @echo file '%MV1%' not found
      @echo file '%MV2%' not found
      @echo file '%MV3%' not found
      REM again depends on logic I am forcing OK exit without parsing
      EXIT /B 0
    ) ELSE (
      SET MV=%MV3%
    )
  ) ELSE (
    SET MV=%MV2%
  )
) ELSE (
  SET MV=%MV1%
)

REM @@@ dending if we got or not SVN revision number call different arguments
rem UTF8 (no diff) VS2010 (Win7) can't handle
rem chcp 65001

rem @echo SVN : "%RELEASE_REVISION%"

SET THISFILE=%0
rem @echo %THISFILE%
if "%RELEASE_REVISION%"=="0" (  
  @REM VS can handle this output and add warning to the list of warnings
  @echo %THISFILE%^(56^) ^: warning SVN001: SVN revision not found!
  rem @echo %MV% -s:MakeVersion.cs -p:SVN:-1
  %MV% -nw -s:MakeVersion.cs -p:SVN:-1
) else (
  rem @echo "SVN:%RELEASE_REVISION%"
  rem @echo %MV% -n -nw -ns -s:MakeVersion.cs -p:SVN:%RELEASE_REVISION%
  %MV% -nw -s:MakeVersion.cs -p:SVN:%RELEASE_REVISION%
)

REM @@@ if makerevision.exe exited with error code exit CMD with same error code.
rem @echo Exit Code is %errorlevel%
if "%errorlevel%" NEQ "0" ( 
  EXIT /B %errorlevel%
)

@echo --- MakeVersion::end --------------------------------------------------
