@echo off
cd /d "%~dp0src"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
"%CSC%" -nologo -codepage:65001 -target:winexe -win32icon:icon.ico -win32manifest:app.manifest -out:..\OptiHub.exe -r:System.Windows.Forms.dll -r:System.Drawing.dll -r:System.Management.dll -r:System.ServiceProcess.dll -r:System.Core.dll Ui.cs Hardware.cs Tweaks.cs MainForm.cs
