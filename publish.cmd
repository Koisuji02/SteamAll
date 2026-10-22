@echo off
rem ==========================================================================================
rem  publish.cmd
rem  Prepara i programmi da pubblicare, UN file solo per sistema, che funzionano senza installare
rem  niente (contengono .NET, Avalonia, il launcher dei giochi e il plugin Millennium):
rem      publish\SteamAll.exe           Windows (64 bit)
rem      publish\linux\SteamAll         Linux (64 bit)
rem  La versione per macOS si prepara su GitHub (vedi .github\workflows\release.yml): Apple vuole
rem  i programmi firmati, e la firma si può fare solo su un Mac.
rem  Prima esegue tutte le prove: se una fallisce, non pubblica.
rem  Uso: doppio clic, oppure "publish.cmd" da un terminale nella cartella del progetto.
rem ==========================================================================================
setlocal
cd /d "%~dp0"

echo Running the tests...
dotnet test -c Release
if errorlevel 1 goto failed

rem   -c Release                 versione ottimizzata
rem   -r win-x64 / linux-x64     per quale sistema
rem   --self-contained true      con .NET dentro: chi lo scarica non deve installare niente
rem   PublishSingleFile          tutto in un file
rem   IncludeNativeLibraries...  anche le librerie native (grafica di Avalonia) dentro il file
rem   EnableCompression...       compresso: pesa meno
set OPTIONS=-c Release --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none

echo.
echo Publishing for Windows...
dotnet publish src\SteamAll.App -r win-x64 -o publish %OPTIONS%
if errorlevel 1 goto failed

echo.
echo Publishing for Linux...
dotnet publish src\SteamAll.App -r linux-x64 -o publish\linux %OPTIONS%
if errorlevel 1 goto failed

rem Le librerie grafiche lasciano accanto al programma i loro file .pdb (servono solo a chi
rem sviluppa quelle librerie): li togliamo, la cartella di pubblicazione contiene solo file generati.
if exist publish\*.pdb del /q publish\*.pdb
if exist publish\linux\*.pdb del /q publish\linux\*.pdb

echo.
echo Done: publish\SteamAll.exe and publish\linux\SteamAll
goto end

:failed
echo.
echo Something failed: see the messages above.

:end
if /i not "%~1"=="nopause" pause
