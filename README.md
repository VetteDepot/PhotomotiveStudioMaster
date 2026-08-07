# Photomotive Studio Master – Event Edition

Offline automotive event production software for Windows.

## Current milestone: Sprint 4 – Vehicle Extraction Checkpoint

The application now includes event management, SD card import, checksum verification, duplicate detection, job numbering, and a first local/offline vehicle extraction path.

### Requirements

- Windows 10/11
- Visual Studio 2022
- .NET 8 SDK
- Visual Studio workload: **.NET desktop development**
- Python 3.11 or 3.12 for the local AI worker

### Open and run

1. Clone or pull the latest repository changes.
2. Open `PhotomotiveStudioMaster.sln` in Visual Studio 2022.
3. In Solution Explorer, right-click `PhotomotiveStudioMaster.App` and choose **Set as Startup Project** if it is not already bold.
4. Select **Debug** and **Any CPU**.
5. Choose **Build > Rebuild Solution**.
6. Confirm the build reports **0 errors**.
7. Use **Ctrl+F5** or launch the built EXE if Visual Studio's debugger keeps the real window behind the XAML Live Preview.

## One-time local AI setup

Sprint 4 uses a local Python worker with rembg/U2Net for the first verified extraction checkpoint.

1. Make sure Python 3.11 or 3.12 is installed and the Windows `py` launcher works.
2. In File Explorer, open the repository folder.
3. Open `tools\ai`.
4. Right-click `Install-AI.ps1` and choose **Run with PowerShell**.

If Windows blocks PowerShell scripts, open PowerShell in the repository folder and run:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\ai\Install-AI.ps1
```

The setup creates a repository-local `.venv`, installs the AI packages, and downloads the U2Net model once. Internet is required for this setup step only. After the model is cached, event-time extraction runs locally without internet.

## Sprint 4 test

1. Create or resume an event.
2. Open **Production**.
3. Import a JPEG, PNG, or TIFF test car image.
4. Confirm the Production header shows **Local AI Ready**.
5. Select the imported job in **Event Processing Queue**.
6. Click **EXTRACT SELECTED**.
7. Click **OPEN EXTRACTED FOLDER**.
8. Verify a transparent PNG named after the job number exists in `04_Extracted`.

This checkpoint intentionally does not extract RAW files yet. RAW preview generation, automatic extraction immediately after import, improved automotive segmentation, GPU acceleration, halo inspection, and extraction quality scoring are subsequent Sprint 4 work.

## Automated build verification

Every push to `main` and every pull request targeting `main` runs a Windows GitHub Actions build using .NET 8.
