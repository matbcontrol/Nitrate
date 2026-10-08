@echo off
rem Frame-budget benchmark at 1080p fullscreen. Put this file next to Nitrate.exe.
rem The CSV is written next to the exe. Add -force-device-index 1 to use the second GPU of a laptop.
start /wait "" "%~dp0Nitrate.exe" -benchmark -screen-fullscreen 1 -screen-width 1920 -screen-height 1080
