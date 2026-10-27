@echo off
rem Builds dist\TextExpander.exe (single file, requires .NET 8 Desktop Runtime).
cd /d "%~dp0"
dotnet publish src\TextExpander\TextExpander.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist
