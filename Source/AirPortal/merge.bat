@echo off
IF EXIST AirPortal_UNMERGED.dll del AirPortal_UNMERGED.dll 
ren AirPortal.dll AirPortal_UNMERGED.dll 
ilmerge /ndebug /out:AirPortal.dll AirPortal_UNMERGED.dll ZeroconfService.dll
