@ECHO OFF

SET SIGNTOOL="C:\Program Files (x86)\Microsoft SDKs\ClickOnce\SignTool\signtool.exe"

IF NOT EXIST "C:\Program Files\7-Zip\7z.exe" (
	ECHO This program requires 7-Zip installed in default path C:\Program Files\7-Zip\7z.exe
	ECHO You may get it at http://www.7-zip.org/
	EXIT /B
)

ECHO Creating AutoACME distribution package...

ECHO Preparing file system...
IF EXIST Distribution RMDIR /Q /S Distribution
IF EXIST AutoACME.zip DEL AutoACME.zip
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
"C:\Program Files\7-Zip\7z.exe" a ..\AutoACME.zip *
CD ..

