# TodoX Deploy CMD Only

This package contains one CMD-only deploy file. No PowerShell.

Copy `Deploy-TodoX-CMD-Only.bat` anywhere, then right click and choose **Run as administrator**.

It will:
- stop IIS
- kill locked processes
- delete `artifacts\publish\TodoX.Web`
- delete `TodoX.Web\publish`
- restore/build/publish Release
- set IIS path to `TodoX.Web\publish`
- start IIS

Log file:
`D:\todoX\Dashboard-web\TodoXPortal\todoX-Dashboard-SaaS\logs\deploy-cmd-only.log`
