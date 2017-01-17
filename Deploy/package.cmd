@ECHO OFF

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

ECHO Making ZIP file...
CD Distribution
"C:\Program Files\7-Zip\7z.exe" a ..\AutoACME.zip *
CD ..

ECHO Cleaning up...
IF EXIST Distribution RMDIR /Q /S Distribution