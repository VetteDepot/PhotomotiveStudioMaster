# Photomotive Studio Master – Event Edition

Offline automotive event production software for Windows.

## Current milestone: Foundation

This milestone establishes a minimal .NET 8 WPF application that should open, build, and run without third-party dependencies.

### Requirements

- Windows 10/11
- Visual Studio 2022
- .NET 8 SDK
- Visual Studio workload: **.NET desktop development**

### Open and run

1. Clone or download this repository.
2. Open `PhotomotiveStudioMaster.sln` in Visual Studio 2022.
3. In Solution Explorer, right-click `PhotomotiveStudioMaster.App` and choose **Set as Startup Project** if it is not already bold.
4. Select **Debug** and **Any CPU**.
5. Choose **Build > Rebuild Solution**.
6. Confirm the build reports **0 errors**.
7. Press **F5**.

You should see the dark Photomotive Studio Master – Event Edition dashboard.

## Automated build verification

Every push to `main` and every pull request targeting `main` runs a Windows GitHub Actions build using .NET 8.

## Next milestone

- Event Manager
- New Event Wizard
- Event folder creation
- SQLite persistence
- Resume active event

The project will be expanded only after the current foundation is verified to build and launch successfully.
