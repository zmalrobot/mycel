# Step 9: Product Rebranding to Mycel, Icon Assets & Platform Packaging

## 1. Executive Summary & Overview
Step 9 executes the comprehensive rebranding of the project to **Mycel**. The rename touches all software layers—solutions, project files, folder structures, C# namespaces, XAML references, diagnostics, build scripts, CI pipelines, and desktop packaging—without introducing behavioural changes, numerical alterations, or feature modifications.

### Core Rebranding Decisions & Invariants
1. **Namespace & Assembly Separation**:
   - Class library: `Mycel.Core` (`src/Mycel.Core/Mycel.Core.csproj`).
   - Desktop application: `Mycel.App` (`src/Mycel.App/Mycel.App.csproj`) with `<AssemblyName>Mycel</AssemblyName>`, producing `Mycel.exe` on Windows and `Mycel` on Linux.
   - Command-line tool: `Mycel.Cli` (`src/Mycel.Cli/Mycel.Cli.csproj`) with `<AssemblyName>mycel-cli</AssemblyName>`, preventing binary collisions.
   - Test suite: `Mycel.Core.Tests` (`tests/Mycel.Core.Tests/Mycel.Core.Tests.csproj`).
   - Solution: `Mycel.sln`.
2. **Backward Compatibility & Binary Persistence**:
   - Legacy run and project files written prior to Step 9 remain 100% readable.
   - The literal 4-byte magic `"TOPO"u8` in `RunWriter.cs` and `RunReader.cs` is preserved byte-for-byte on disk.
   - Project loading does not enforce application name identity, allowing legacy `project.json` files to open transparently.
   - Verified by `tests/Mycel.Core.Tests/Legacy/LegacyFormatTests.cs`.
3. **App-Data & Logs Migration**:
   - Active user directory: `%LOCALAPPDATA%/Mycel` on Windows, `~/.local/share/Mycel` on Linux.
   - Safe migration in `CrashLogger.cs`: if the legacy application directory exists and the new `Mycel` directory does not, user settings are copied once (logs are excluded, and the legacy directory is never deleted).
4. **Source Code Hygiene (Forbidden Word Scan)**:
   - Automated regression test `tests/Mycel.Core.Tests/SourceScanTests.cs` scans the entire repository to ensure 0 occurrences of the former product name outside an explicit allowlist (`docs/RENAME_PLAN.md`, migration fallback paths in `CrashLogger.cs`/`OrbitDiagnostics.cs`, and legacy test fixtures).

---

## 2. Icon Assets Generation
The master visual asset is `resources/logo.jpeg` ($2048 \times 2048$, square, 8-bit sRGB).

- **Tooling**:
  - `tools/make-icons` (Bash) and `tools/make-icons.ps1` (PowerShell).
  - Generates PNGs at 16, 24, 32, 48, 64, 128, 256, and 512 px into `src/Mycel.App/Assets/icons/`.
  - Generates multi-resolution `mycel.ico` with PNG-compressed entries at 16, 24, 32, 48, 64, 128, and 256 px.
  - Applies a continuous rounded-square alpha mask (corner radius $\approx 20\%$ of side length) with Lanczos high-quality resampling, switchable via `--no-mask`.
  - Assets are marked as `<AvaloniaResource Include="Assets\**" />` in `Mycel.App.csproj`.

---

## 3. Platform Integration & Packaging

### Windows Integration
- Application manifest & PE metadata: `<ApplicationIcon>` pointing to `mycel.ico`, `<AssemblyTitle>Mycel</AssemblyTitle>`, `<Product>Mycel</Product>`, and company/copyright metadata inherited from `Directory.Build.props`.
- Window icon wired to 256-px PNG resource (`Icon="avares://Mycel/Assets/icons/mycel_256.png"`).
- About dialog presents product name and the 64-px branded icon alongside GPLv3 and disclaimer notices.
- Binary inspection confirms `Mycel.exe` contains valid `VS_VERSION_INFO`, `FileDescription`, `ProductVersion`, and `CompanyName`.
- **Note on Cross-Publishing**: Publishing `win-x64` from Linux bundles complete PE version metadata; for optimal native resource embedding, release builds are produced on Windows runners (`windows-latest`).

### Linux Integration
- Published single-file apphost is named `Mycel`.
- Window manager matching: configured in `Program.cs` via `X11PlatformOptions.WmClass = "Mycel"`.
- Desktop entry: `packaging/linux/mycel.desktop` with `StartupWMClass=Mycel` and `Icon=mycel`.
- User installation scripts:
  - `packaging/linux/install.sh`: installs binary to `~/.local/opt/mycel`, registers desktop entry in `~/.local/share/applications`, installs hicolor PNG icons in `~/.local/share/icons/hicolor/*/apps/mycel.png`, and updates icon caches.
  - `packaging/linux/uninstall.sh`: cleanly removes all installed files and refreshes desktop databases.
- Distribution archives:
  - `dist/Mycel-<version>-linux-x64.tar.gz` (contains `Mycel`, `packaging/linux/*`, icon PNGs, `LICENSE`, and `THIRD_PARTY_NOTICES.md`).
  - `dist/Mycel-<version>-win-x64.zip` (contains `Mycel.exe`, `LICENSE`, and `THIRD_PARTY_NOTICES.md`).

---

## 4. Automated Verification Results

- **Compiler Status**: `dotnet build -c Release` passes with **0 warnings and 0 errors**.
- **Automated Test Suite**:
  ```text
  Passed! - Failed: 0, Passed: 167, Skipped: 4, Total: 171 (Duration: ~23s)
  ```
  - 164 baseline verification tests.
  - +2 legacy format compatibility tests (`LegacyFormatTests`).
  - +1 repository source code scan test (`SourceScanTests`).
  - 4 long-running benchmarks excluded by default via `--filter "Category!=Benchmark"`.

---

## 5. Manual Verification Checklist

### On Linux (X11 & Wayland)
1. **Window Title & Icon**: Launch `./Mycel` or `dotnet run --project src/Mycel.App/Mycel.App.csproj -c Release`. Confirm the window title bar displays `"Mycel — Topology Optimization"` and the taskbar shows the Mycel rounded-square icon.
2. **About Dialog**: Open About dialog from the toolbar; verify the 64-px Mycel icon renders sharply next to `"Mycel v1.0.0"`.
3. **User Installation**: Run `./packaging/linux/install.sh`. Confirm Mycel appears in the desktop application launcher (GNOME Shell / KDE / App menu) with the correct icon and launches properly. Run `./packaging/linux/uninstall.sh` and verify clean removal.

### On Windows 10/11
1. **File Explorer & Properties**: Extract `dist/Mycel-1.0.0-win-x64.zip`. Verify `Mycel.exe` displays the rounded Mycel icon in File Explorer. Right-click $\to$ Properties $\to$ Details: confirm File Description is `"Mycel"`, Product Name is `"Mycel"`, and Version is `"1.0.0.0"`.
2. **Taskbar & Alt+Tab**: Launch `Mycel.exe`. Confirm the taskbar icon, Alt+Tab thumbnail, and Task Manager process name show `"Mycel"`.
3. **STL Export Header**: Export an STL from a completed optimization and verify the binary header begins with `"Mycel it=..."`.

