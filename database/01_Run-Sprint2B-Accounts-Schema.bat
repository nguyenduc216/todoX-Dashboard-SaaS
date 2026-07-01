@echo off
title TodoX SaaS - Sprint 2B Database
setlocal

set PGHOST=113.160.249.61
set PGPORT=5432
set PGDATABASE=todo_saas
set PGUSER=todox_user

echo ===========================================
echo TodoX Sprint 2B database schema and seed
echo ===========================================
echo.
echo Target: %PGHOST%:%PGPORT%/%PGDATABASE%
echo User:   %PGUSER%
echo.
set /p PGPASSWORD=Enter PostgreSQL password for todox_user: 

psql -h %PGHOST% -p %PGPORT% -U %PGUSER% -d %PGDATABASE% -f sprint2b_accounts_schema_seed.sql

if errorlevel 1 (
  echo.
  echo Database script failed.
  pause
  exit /b 1
)

echo.
echo Database script completed.
pause
