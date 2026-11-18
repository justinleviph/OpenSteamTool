STEAM TOOL MANAGER — SIMPLE WINDOWS CONFIGURATION GUI
======================================================

A standalone C# / .NET 8 WinForms desktop app that manages local OpenSteamTool
files and Lua configuration, with no remote download or account sign-in.

FEATURES
- Detect / browse Steam installation directory
- Inspect OpenSteamTool DLL presence
- Optionally install three DLLs from a locally selected directory, after a
  confirmation and backing up any existing files
- Uninstall only DLLs that match the copies installed by this manager;
  restore backed-up files where applicable
- Create, view, and remove GUI-managed AppID Lua configs
- List and edit Lua scripts from Steam/config/lua
- Edit opensteamtool.toml
- View available debug logs
- Back up Lua and TOML config into the user's AppData

IMPORTANT
- OpenSteamTool and its DLLs are NOT included. You must source them separately.
- This app does NOT confer game ownership or guarantee DRM compatibility.
- Modifying Steam's installation can create security, compatibility,
  licensing and Steam account risks. Verify DLL provenance before installation.
- Stop Steam completely before installing or removing DLLs.
- For installation under Program Files, run as administrator if required.
- Never use untrusted Lua scripts or DLLs.
- This is an independent, unofficial configuration utility.

HOW TO BUILD ONLINE (NO VISUAL STUDIO)
1) Extract the SteamToolManager-Project.zip project.
2) Make a new GitHub repository, e.g. SteamToolManager-GUI.
3) Upload project contents, preserving the .github/workflows path.
4) Open GitHub Actions > Windows EXE Build > Run workflow (main branch).
5) From the completed run, download SteamToolManager-Windows-x64 artifact.
6) Unzip the downloaded artifact to get SteamToolManager-Windows-x64.zip;
   extract it for SteamToolManager.exe and README.txt.

This is a self-contained Windows x64 .NET publish. No Visual Studio or separate
.NET runtime installation is required on the target Windows machine.

DEVELOPERS
- Framework: .NET 8 Windows Forms
- Build: dotnet publish SteamToolManager/SteamToolManager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false
