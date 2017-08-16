@ECHO OFF

SET SIGNTOOL="C:\Program Files (x86)\Microsoft SDKs\ClickOnce\SignTool\signtool.exe"
IF NOT EXIST %SIGNTOOL% (
	ECHO This program requires SignTool installed at %SEVENZ%
	ECHO The SignTool is part of Windows SDK
	EXIT /B
)

SET SEVENZ="C:\Program Files\7-Zip\7z.exe"
IF NOT EXIST %SEVENZ% (
	ECHO This program requires 7-Zip installed at %SEVENZ%
	ECHO You may get it at http://www.7-zip.org/
	EXIT /B
)

ECHO Creating AutoACME distribution package...

ECHO Preparing file system...
IF EXIST Distribution RMDIR /Q /S Distribution
IF EXIST AutoACME.zip DEL AutoACME.zip
IF EXIST AutoACME-setup.exe DEL AutoACME-setup.exe
MKDIR Distribution\lib

ECHO Copying documentation files...
COPY /Y ..\README.md Distribution
COPY /Y ..\LICENSE Distribution

ECHO Copying Altairis.AutoAcme.Manager files...
COPY /Y ..\Altairis.AutoAcme.Manager\bin\Debug\*.dll Distribution\lib
COPY /Y ..\Altairis.AutoAcme.Manager\bin\Debug\autoacme.exe Distribution
COPY /Y ..\Altairis.AutoAcme.Manager\bin\Debug\autoacme.exe.config Distribution
ECHO.

ECHO Copying Altairis.AutoAcme.IisSync files...
COPY /Y ..\Altairis.AutoAcme.IisSync\bin\Debug\*.dll Distribution\lib
COPY /Y ..\Altairis.AutoAcme.IisSync\bin\Debug\aasync.exe Distribution
COPY /Y ..\Altairis.AutoAcme.IisSync\bin\Debug\aasync.exe.config Distribution

ECHO Digitally signing EXE files...
%SIGNTOOL% sign /n "Altairis, s. r. o." /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 "Distribution\autoacme.exe"
%SIGNTOOL% sign /n "Altairis, s. r. o." /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 "Distribution\aasync.exe"
%SIGNTOOL% sign /n "Altairis, s. r. o." /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 "Distribution\lib\Altairis.AutoAcme.Core.dll"
%SIGNTOOL% sign /n "Altairis, s. r. o." /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 "Distribution\lib\Altairis.AutoAcme.Configuration.dll"

ECHO Making ZIP file...
CD Distribution
%SEVENZ% a ..\AutoACME.zip *
%SEVENZ% a -sfx"..\AutoACME.sfx" ..\AutoACME-setup.exe *
CD ..

ECHO Digitally signing SFX archive...
%SIGNTOOL% sign /n "Altairis, s. r. o." /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 "AutoACME-setup.exe"