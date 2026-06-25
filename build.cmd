@echo off
setlocal

set CSC="C:\Program Files\Microsoft Visual Studio\2022\Preview\MSBuild\Current\Bin\Roslyn\csc.exe"
set UM=D:\SteamLibrary\steamapps\common\Unturned\Unturned_Data\Managed
set RD=D:\SteamLibrary\steamapps\common\Unturned\Extras\Rocket.Unturned
set FW=C:\Windows\Microsoft.NET\Framework64\v4.0.30319
set PLUGINS=D:\SteamLibrary\steamapps\common\U3DS\Servers\Default\Rocket\Plugins

if not exist "%~dp0bin"  mkdir "%~dp0bin"
if not exist "%~dp0dist" mkdir "%~dp0dist"

%CSC% /nologo /noconfig /nostdlib+ /target:library /langversion:latest /optimize+ ^
  /out:"%~dp0bin\VaultBackpack.dll" ^
  /reference:"%FW%\mscorlib.dll" ^
  /reference:"%FW%\System.dll" ^
  /reference:"%FW%\System.Core.dll" ^
  /reference:"%FW%\System.Xml.dll" ^
  /reference:"%FW%\System.Data.dll" ^
  /reference:"%UM%\netstandard.dll" ^
  /reference:"%UM%\Assembly-CSharp.dll" ^
  /reference:"%UM%\UnityEngine.dll" ^
  /reference:"%UM%\UnityEngine.CoreModule.dll" ^
  /reference:"%UM%\UnityEngine.PhysicsModule.dll" ^
  /reference:"%UM%\com.rlabrecque.steamworks.net.dll" ^
  /reference:"%RD%\Rocket.API.dll" ^
  /reference:"%RD%\Rocket.Core.dll" ^
  /reference:"%RD%\Rocket.Unturned.dll" ^
  /reference:"%~dp0lib\MySql.Data.dll" ^
  "%~dp0AssemblyInfo.cs" ^
  "%~dp0VaultBackpackConfig.cs" ^
  "%~dp0VaultDatabase.cs" ^
  "%~dp0VaultBPComponent.cs" ^
  "%~dp0VaultBackpackPlugin.cs" ^
  "%~dp0CommandVault.cs" ^
  "%~dp0CommandBackpackUpgrade.cs" ^
  "%~dp0CommandBackpackSet.cs"

if errorlevel 1 ( echo BUILD FAILED & exit /b 1 )

copy /Y "%~dp0bin\VaultBackpack.dll"  "%~dp0dist\VaultBackpack.dll"  >nul
copy /Y "%~dp0lib\MySql.Data.dll"     "%~dp0dist\MySql.Data.dll"     >nul

echo.
echo Built: dist\VaultBackpack.dll

REM --- deploy to server (comment out if not ready) ---
REM if not exist "%PLUGINS%\VaultBackpack" mkdir "%PLUGINS%\VaultBackpack"
REM copy /Y "%~dp0dist\VaultBackpack.dll" "%PLUGINS%\VaultBackpack.dll" >nul
REM copy /Y "%~dp0dist\MySql.Data.dll"    "%PLUGINS%\MySql.Data.dll"    >nul
REM echo Deployed to server.

endlocal
