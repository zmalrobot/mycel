# Product Rename Plan: TopoOpt → Mycel

## 1. Executive Summary & Preflight Baseline

- **Target Branch**: `rename/mycel`
- **Product Name**: `Mycel`
- **Product Subtitle**: `Topology Optimization`
- **Visual Identity**: `resources/logo.jpeg` (2048 × 2048 px, square, 24-bit RGB)
- **Baseline Build Verification**:
  - Command: `dotnet build -c Release`
  - Result: **0 Warning(s), 0 Error(s)** (Build succeeded in 15.23s)
- **Baseline Test Verification**:
  - Command: `dotnet test`
  - Result: **164 Passed, 0 Failed, 4 Skipped, Total 168** (Duration: 1m 57s)

---

## 2. Inventory of TopoOpt Occurrences by Category

Total occurrences in source tree (excluding `.git`, `bin`, `obj`, `dist`, `artifacts`, `runs`, `*.stl`): **510 occurrences across 139 files**.

### Solution, Projects, Folders and File Names
| Type | Current Path | Target Path |
|------|--------------|-------------|
| **Directory** | `src/TopoOpt.Core` | `src/Mycel.Core` |
| **Directory** | `src/TopoOpt.App` | `src/Mycel.App` |
| **Directory** | `src/TopoOpt.Cli` | `src/Mycel.Cli` |
| **Directory** | `tests/TopoOpt.Core.Tests` | `tests/Mycel.Core.Tests` |
| **Solution** | `TopoOpt.sln` | `Mycel.sln` |
| **Project** | `src/TopoOpt.Core/TopoOpt.Core.csproj` | `src/Mycel.Core/Mycel.Core.csproj` |
| **Project** | `src/TopoOpt.App/TopoOpt.App.csproj` | `src/Mycel.App/Mycel.App.csproj` |
| **Project** | `src/TopoOpt.Cli/TopoOpt.Cli.csproj` | `src/Mycel.Cli/Mycel.Cli.csproj` |
| **Project** | `tests/TopoOpt.Core.Tests/TopoOpt.Core.Tests.csproj` | `tests/Mycel.Core.Tests/Mycel.Core.Tests.csproj` |

### 1. Solution and Project References
- **Total Matches**: 20 across 4 file(s)
| File Path | Line | Occurrence | Context |
|-----------|------|------------|---------|
| `TopoOpt.sln` | 8 | `TopoOpt` | `Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "TopoOpt.Core", "src\TopoOpt.Core\TopoOpt.Core.csproj", "{C72E4238-9D9B-43C4-AFE6-AA3B7BEB1EF7}"` |
| `TopoOpt.sln` | 8 | `TopoOpt` | `Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "TopoOpt.Core", "src\TopoOpt.Core\TopoOpt.Core.csproj", "{C72E4238-9D9B-43C4-AFE6-AA3B7BEB1EF7}"` |
| `TopoOpt.sln` | 8 | `TopoOpt` | `Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "TopoOpt.Core", "src\TopoOpt.Core\TopoOpt.Core.csproj", "{C72E4238-9D9B-43C4-AFE6-AA3B7BEB1EF7}"` |
| `TopoOpt.sln` | 10 | `TopoOpt` | `Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "TopoOpt.Cli", "src\TopoOpt.Cli\TopoOpt.Cli.csproj", "{7BBFDEEB-5424-4B1B-8F45-C9BE15765374}"` |
| `TopoOpt.sln` | 10 | `TopoOpt` | `Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "TopoOpt.Cli", "src\TopoOpt.Cli\TopoOpt.Cli.csproj", "{7BBFDEEB-5424-4B1B-8F45-C9BE15765374}"` |
| `TopoOpt.sln` | 10 | `TopoOpt` | `Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "TopoOpt.Cli", "src\TopoOpt.Cli\TopoOpt.Cli.csproj", "{7BBFDEEB-5424-4B1B-8F45-C9BE15765374}"` |
| `TopoOpt.sln` | 12 | `TopoOpt` | `Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "TopoOpt.App", "src\TopoOpt.App\TopoOpt.App.csproj", "{B8117073-97C0-41EE-B95E-C88750D24944}"` |
| `TopoOpt.sln` | 12 | `TopoOpt` | `Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "TopoOpt.App", "src\TopoOpt.App\TopoOpt.App.csproj", "{B8117073-97C0-41EE-B95E-C88750D24944}"` |
| `TopoOpt.sln` | 12 | `TopoOpt` | `Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "TopoOpt.App", "src\TopoOpt.App\TopoOpt.App.csproj", "{B8117073-97C0-41EE-B95E-C88750D24944}"` |
| `TopoOpt.sln` | 16 | `TopoOpt` | `Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "TopoOpt.Core.Tests", "tests\TopoOpt.Core.Tests\TopoOpt.Core.Tests.csproj", "{E1331CDA-E281-4614-9544-A4FC5BB1817D}"` |
| `TopoOpt.sln` | ... | ... | *(and 2 more occurrences)* |
| `src/TopoOpt.App/TopoOpt.App.csproj` | 22 | `TopoOpt` | `<ProjectReference Include="..\TopoOpt.Core\TopoOpt.Core.csproj" />` |
| `src/TopoOpt.App/TopoOpt.App.csproj` | 22 | `TopoOpt` | `<ProjectReference Include="..\TopoOpt.Core\TopoOpt.Core.csproj" />` |
| `src/TopoOpt.Cli/TopoOpt.Cli.csproj` | 11 | `TopoOpt` | `<ProjectReference Include="..\TopoOpt.Core\TopoOpt.Core.csproj" />` |
| `src/TopoOpt.Cli/TopoOpt.Cli.csproj` | 11 | `TopoOpt` | `<ProjectReference Include="..\TopoOpt.Core\TopoOpt.Core.csproj" />` |
| `tests/TopoOpt.Core.Tests/TopoOpt.Core.Tests.csproj` | 18 | `TopoOpt` | `<ProjectReference Include="..\..\src\TopoOpt.Core\TopoOpt.Core.csproj" />` |
| `tests/TopoOpt.Core.Tests/TopoOpt.Core.Tests.csproj` | 18 | `TopoOpt` | `<ProjectReference Include="..\..\src\TopoOpt.Core\TopoOpt.Core.csproj" />` |
| `tests/TopoOpt.Core.Tests/TopoOpt.Core.Tests.csproj` | 19 | `TopoOpt` | `<ProjectReference Include="..\..\src\TopoOpt.App\TopoOpt.App.csproj" />` |
| `tests/TopoOpt.Core.Tests/TopoOpt.Core.Tests.csproj` | 19 | `TopoOpt` | `<ProjectReference Include="..\..\src\TopoOpt.App\TopoOpt.App.csproj" />` |

### 10. Build Scripts, CI Workflows & Packaging
- **Total Matches**: 30 across 3 file(s)
| File Path | Line | Occurrence | Context |
|-----------|------|------------|---------|
| `build.ps1` | 1 | `TopoOpt` | `# TopoOpt Release Packaging Script for PowerShell` |
| `build.ps1` | 16 | `TopoOpt` | `Write-Host " Building TopoOpt v$version Release Packages" -ForegroundColor Cyan` |
| `build.ps1` | 28 | `TopoOpt` | `dotnet publish src/TopoOpt.App/TopoOpt.App.csproj `` |
| `build.ps1` | 28 | `TopoOpt` | `dotnet publish src/TopoOpt.App/TopoOpt.App.csproj `` |
| `build.ps1` | 41 | `TopoOpt` | `Write-Host "Creating dist/TopoOpt-$version-linux-x64.tar.gz..."` |
| `build.ps1` | 42 | `TopoOpt` | `tar -czf "dist/TopoOpt-$version-linux-x64.tar.gz" -C artifacts/linux-x64 .` |
| `build.ps1` | 45 | `TopoOpt` | `dotnet publish src/TopoOpt.App/TopoOpt.App.csproj `` |
| `build.ps1` | 45 | `TopoOpt` | `dotnet publish src/TopoOpt.App/TopoOpt.App.csproj `` |
| `build.ps1` | 58 | `TopoOpt` | `Write-Host "Creating dist/TopoOpt-$version-win-x64.zip..."` |
| `build.ps1` | 59 | `TopoOpt` | `Compress-Archive -Path "artifacts/win-x64/*" -DestinationPath "dist/TopoOpt-$version-win-x64.zip" -Force` |
| `build.ps1` | ... | ... | *(and 3 more occurrences)* |
| `build.sh` | 16 | `TopoOpt` | `echo " Building TopoOpt v${VERSION} Release Packages"` |
| `build.sh` | 29 | `TopoOpt` | `dotnet publish src/TopoOpt.App/TopoOpt.App.csproj \` |
| `build.sh` | 29 | `TopoOpt` | `dotnet publish src/TopoOpt.App/TopoOpt.App.csproj \` |
| `build.sh` | 42 | `TopoOpt` | `echo "Creating dist/TopoOpt-${VERSION}-linux-x64.tar.gz..."` |
| `build.sh` | 43 | `TopoOpt` | `tar -czf "dist/TopoOpt-${VERSION}-linux-x64.tar.gz" -C artifacts/linux-x64 .` |
| `build.sh` | 47 | `TopoOpt` | `dotnet publish src/TopoOpt.App/TopoOpt.App.csproj \` |
| `build.sh` | 47 | `TopoOpt` | `dotnet publish src/TopoOpt.App/TopoOpt.App.csproj \` |
| `build.sh` | 60 | `TopoOpt` | `echo "Creating dist/TopoOpt-${VERSION}-win-x64.zip..."` |
| `build.sh` | 61 | `TopoOpt` | `(cd artifacts/win-x64 && zip -q -r "../../dist/TopoOpt-${VERSION}-win-x64.zip" .)` |
| `build.sh` | 65 | `TopoOpt` | `ls -lh dist/TopoOpt-${VERSION}-linux-x64.tar.gz` |
| `build.sh` | ... | ... | *(and 2 more occurrences)* |
| `run.sh` | 8 | `TopoOpt` | `echo " TopoOpt Clean Build & Launch"` |
| `run.sh` | 17 | `TopoOpt` | `echo "--> Building TopoOpt in Release mode..."` |
| `run.sh` | 20 | `TopoOpt` | `echo "--> Launching TopoOpt.App..."` |
| `run.sh` | 21 | `TopoOpt` | `exec dotnet run --project src/TopoOpt.App/TopoOpt.App.csproj -c Release --no-build -- "$@"` |
| `run.sh` | 21 | `TopoOpt` | `exec dotnet run --project src/TopoOpt.App/TopoOpt.App.csproj -c Release --no-build -- "$@"` |

### 11. Documentation, AGENTS.md, README & Notices
- **Total Matches**: 120 across 22 file(s)
| File Path | Line | Occurrence | Context |
|-----------|------|------------|---------|
| `AGENTS.md` | 1 | `TopoOpt` | `# TopoOpt Agent Development Rules` |
| `AGENTS.md` | 23 | `TopoOpt` | `- **Multi-Unit Load Inputs & Physical Grounding**: `TopoOpt.Core` and `.topo` project files MUST store load forces exclusively in Newtons ($N$). UI layers projecting alternative units (such as equivalent mass in $\text{kg}$ or $\text{g}$ via $F = m \cdot g_0$) must use standard gravitational acceleration $g_0 = 9.80665\text{ m/s}^2$ (ISO 80000-3 / NIST) and guard bidirectional property updates with an `_isSyncing` reentrancy flag to avoid circular notification cascades and precision drift.` |
| `AGENTS.md` | 32 | `TopoOpt` | `- `TopoOpt.Core` MUST NOT reference any UI framework (no Avalonia, no WPF, no Silk.NET, no OpenGL).` |
| `CONTRIBUTING.md` | 1 | `TopoOpt` | `# Contributing to TopoOpt` |
| `CONTRIBUTING.md` | 3 | `TopoOpt` | `Thank you for contributing to **TopoOpt**. This document outlines the build workflow, test execution, architectural invariants, and coding conventions taken from `AGENTS.md`.` |
| `CONTRIBUTING.md` | 9 | `TopoOpt` | `TopoOpt targets **.NET 10.0** (`10.0.x`).` |
| `CONTRIBUTING.md` | 70 | `TopoOpt` | `- **UI-Free Core**: `TopoOpt.Core` **MUST NOT** reference any UI or graphics framework (no Avalonia, no WPF, no Silk.NET, no OpenGL).` |
| `CONTRIBUTING.md` | 86 | `TopoOpt` | `1. **Choose the Matching Test Folder**: Place new tests in `tests/TopoOpt.Core.Tests/<Subsystem>/` matching the `TopoOpt.Core` namespace (`Geometry`, `Voxelization`, `Fem`, `Optimization`, `Workflow`, `Step6`, or `Diagnostics`).` |
| `CONTRIBUTING.md` | 86 | `TopoOpt` | `1. **Choose the Matching Test Folder**: Place new tests in `tests/TopoOpt.Core.Tests/<Subsystem>/` matching the `TopoOpt.Core` namespace (`Geometry`, `Voxelization`, `Fem`, `Optimization`, `Workflow`, `Step6`, or `Diagnostics`).` |
| `THIRD_PARTY_NOTICES.md` | 3 | `TopoOpt` | `TopoOpt includes or links to third-party open-source components under permissive open-source licences. This document lists the third-party dependencies, their authors, and their respective licence terms.` |
| `docs/ARCHITECTURE.md` | 1 | `TopoOpt` | `# TopoOpt Architecture Specification` |
| `docs/ARCHITECTURE.md` | 7 | `TopoOpt` | `│   ├── TopoOpt.Core/           # net10.0 class library: Pure numerics, voxelization, FEM, SIMP, zero UI deps` |
| `docs/ARCHITECTURE.md` | 15 | `TopoOpt` | `│   ├── TopoOpt.App/            # net10.0 Avalonia desktop application (MVVM, dark UI, 3D viewport)` |
| `docs/ARCHITECTURE.md` | 21 | `TopoOpt` | `│   └── TopoOpt.Cli/            # net10.0 headless CLI dev tool: benchmark runner, batch jobs, validation` |
| `docs/ARCHITECTURE.md` | 23 | `TopoOpt` | `│   └── TopoOpt.Core.Tests/     # xUnit test suite: analytical benchmarks, beam deflections, SIMP tests` |
| `docs/ARCHITECTURE.md` | 27 | `TopoOpt` | `└── TopoOpt.sln` |
| `docs/ARCHITECTURE.md` | 46 | `TopoOpt` | `namespace TopoOpt.Core.Geometry;` |
| `docs/ARCHITECTURE.md` | 79 | `TopoOpt` | `namespace TopoOpt.Core.Voxelization;` |
| `docs/ARCHITECTURE.md` | 104 | `TopoOpt` | `namespace TopoOpt.Core.Fem;` |
| `docs/ARCHITECTURE.md` | 150 | `TopoOpt` | `namespace TopoOpt.Core.Optimization;` |
| `docs/ARCHITECTURE.md` | ... | ... | *(and 2 more occurrences)* |
| `docs/DECISIONS.md` | 75 | `TopoOpt` | `- **Decision**: License TopoOpt under the **GNU General Public License v3.0 (GPLv3)**.` |
| `docs/HOTFIX_ORBIT.md` | 30 | `TopoOpt` | `Captured from `~/.local/share/TopoOpt/logs/orbit_diag.log` during in-window execution:` |
| `docs/HOTFIX_ORBIT.md` | 42 | `TopoOpt` | `[   2709.70 ms] this.InputHitTest at 490, 385 returned: TopoOpt.App.Controls.Viewport3DControl (Name=Viewport)` |
| `docs/HOTFIX_ORBIT.md` | 43 | `TopoOpt` | `[   2709.74 ms] Viewport.InputHitTest at (100, 100) returned: TopoOpt.App.Controls.Viewport3DControl (Name=Viewport)` |
| `docs/HOTFIX_ORBIT.md` | 89 | `TopoOpt` | ``tests/TopoOpt.Core.Tests/Geometry/OrbitCameraTests.cs`:` |
| `docs/HOTFIX_STEPPER.md` | 7 | `TopoOpt` | `Evidence first, same as `HOTFIX_ORBIT.md`: debug switch `--test-stepper` (`MainWindow.RunStepperDiagnosticTestAsync`, log `~/.local/share/TopoOpt/logs/stepper_diag.log`; `--diag-pointer` adds a 25 s pointer-event trace for manual runs).` |
| `docs/HOTFIX_STEPPER.md` | 38 | `TopoOpt` | ``tests/TopoOpt.Core.Tests/App/StepperTests.cs` (13 cases): string parameter is executable and activates exactly one step; invalid parameters are ignored; `PropertyChanged` for all flags; XAML regression guards (no `ActiveStep` + `ObjectConverters.Equal` + numeric parameter, no typed `SelectStepCommand`, bodies bound to typed flags, headers without local `Background`). Existing suite unchanged.` |
| `docs/INSTALL.md` | 1 | `TopoOpt` | `# TopoOpt Installation & Runtime Requirements` |
| `docs/INSTALL.md` | 3 | `TopoOpt` | `This document describes the prerequisites, installation steps, and graphics troubleshooting for running **TopoOpt** on Linux and Windows platforms.` |
| `docs/INSTALL.md` | 9 | `TopoOpt` | `TopoOpt is distributed as a self-contained single-file executable (`dist/TopoOpt-<version>-linux-x64.tar.gz`), requiring no external .NET runtime installation. However, it requires standard Linux desktop graphics and font libraries:` |
| `docs/INSTALL.md` | 9 | `TopoOpt` | `TopoOpt is distributed as a self-contained single-file executable (`dist/TopoOpt-<version>-linux-x64.tar.gz`), requiring no external .NET runtime installation. However, it requires standard Linux desktop graphics and font libraries:` |
| `docs/INSTALL.md` | 45 | `TopoOpt` | `If running on a virtual machine, headless server, or legacy GPU without OpenGL 3.3 hardware support, TopoOpt detects OpenGL initialization errors and warns rather than crashing.` |
| `docs/INSTALL.md` | 49 | `TopoOpt` | `LIBGL_ALWAYS_SOFTWARE=1 ./TopoOpt` |
| `docs/INSTALL.md` | 55 | `TopoOpt` | `AVALONIA_SCREEN_BACKEND=x11 ./TopoOpt` |
| `docs/INSTALL.md` | 58 | `TopoOpt` | `AVALONIA_SCREEN_BACKEND=wayland ./TopoOpt` |
| `docs/INSTALL.md` | 67 | `TopoOpt` | `- TopoOpt for Windows (`dist/TopoOpt-<version>-win-x64.zip`) is fully self-contained; no .NET Framework or .NET Desktop Runtime installation is required.` |
| `docs/INSTALL.md` | 67 | `TopoOpt` | `- TopoOpt for Windows (`dist/TopoOpt-<version>-win-x64.zip`) is fully self-contained; no .NET Framework or .NET Desktop Runtime installation is required.` |
| `docs/QA.md` | 1 | `TopoOpt` | `# TopoOpt Quality Assurance & Verification Checklist` |
| `docs/QA.md` | 20 | `TopoOpt` | `\| **QA-12** \| **Release Package Integrity** \| 1. Build release packages via `build.sh` / `build.ps1`.<br>2. Verify single-file self-contained archives in `dist/`. \| Generates `TopoOpt-1.0.0-linux-x64.tar.gz` and `TopoOpt-1.0.0-win-x64.zip` containing executable, `LICENSE`, and `THIRD_PARTY_NOTICES.md`. \| **PASS** (Verified) \| **PASS** (Ready) \|` |
| `docs/QA.md` | 20 | `TopoOpt` | `\| **QA-12** \| **Release Package Integrity** \| 1. Build release packages via `build.sh` / `build.ps1`.<br>2. Verify single-file self-contained archives in `dist/`. \| Generates `TopoOpt-1.0.0-linux-x64.tar.gz` and `TopoOpt-1.0.0-win-x64.zip` containing executable, `LICENSE`, and `THIRD_PARTY_NOTICES.md`. \| **PASS** (Verified) \| **PASS** (Ready) \|` |
| `docs/RESEARCH.md` | 7 | `TopoOpt` | `- **Recommendation**: Target `net10.0` for all projects (`TopoOpt.Core`, `TopoOpt.App`, `TopoOpt.Cli`, `TopoOpt.Core.Tests`). Use Avalonia 12.1.4 with MVVM CommunityToolkit.` |
| `docs/RESEARCH.md` | 7 | `TopoOpt` | `- **Recommendation**: Target `net10.0` for all projects (`TopoOpt.Core`, `TopoOpt.App`, `TopoOpt.Cli`, `TopoOpt.Core.Tests`). Use Avalonia 12.1.4 with MVVM CommunityToolkit.` |
| `docs/RESEARCH.md` | 7 | `TopoOpt` | `- **Recommendation**: Target `net10.0` for all projects (`TopoOpt.Core`, `TopoOpt.App`, `TopoOpt.Cli`, `TopoOpt.Core.Tests`). Use Avalonia 12.1.4 with MVVM CommunityToolkit.` |
| `docs/RESEARCH.md` | 7 | `TopoOpt` | `- **Recommendation**: Target `net10.0` for all projects (`TopoOpt.Core`, `TopoOpt.App`, `TopoOpt.Cli`, `TopoOpt.Core.Tests`). Use Avalonia 12.1.4 with MVVM CommunityToolkit.` |
| `docs/RESEARCH.md` | 56 | `TopoOpt` | `- **Recommendation**: Implement SIMP + Density Filter + OC with bisection in `TopoOpt.Core`. For FDM (0.4 mm nozzle), set filter radius $R_{min} \ge 1.2$ mm ($\ge 2\times$ nozzle width / voxel size).` |
| `docs/ROADMAP.md` | 1 | `TopoOpt` | `# TopoOpt Implementation Roadmap` |
| `docs/ROADMAP.md` | 5 | `TopoOpt` | `- Solution scaffolding (`TopoOpt.Core`, `TopoOpt.App`, `TopoOpt.Cli`, `TopoOpt.Core.Tests`).` |
| `docs/ROADMAP.md` | 5 | `TopoOpt` | `- Solution scaffolding (`TopoOpt.Core`, `TopoOpt.App`, `TopoOpt.Cli`, `TopoOpt.Core.Tests`).` |
| `docs/ROADMAP.md` | 5 | `TopoOpt` | `- Solution scaffolding (`TopoOpt.Core`, `TopoOpt.App`, `TopoOpt.Cli`, `TopoOpt.Core.Tests`).` |
| `docs/ROADMAP.md` | 5 | `TopoOpt` | `- Solution scaffolding (`TopoOpt.Core`, `TopoOpt.App`, `TopoOpt.Cli`, `TopoOpt.Core.Tests`).` |
| `docs/ROADMAP.md` | 60 | `TopoOpt` | `- `TopoOpt.Cli` console tool to run end-to-end optimizations headless with progress logging.` |
| `docs/STEP1.md` | 5 | `TopoOpt` | `- `src/TopoOpt.Core`: Pure numerical and geometrical library with zero UI dependencies.` |
| `docs/STEP1.md` | 6 | `TopoOpt` | `- `src/TopoOpt.App`: Avalonia 12.1.4 desktop application (Dark Fluent theme, MVVM with `CommunityToolkit.Mvvm`, `Silk.NET.OpenGL` 2.23.0).` |
| `docs/STEP1.md` | 7 | `TopoOpt` | `- `src/TopoOpt.Cli`: Headless CLI benchmark and STL inspection utility.` |
| `docs/STEP1.md` | 8 | `TopoOpt` | `- `tests/TopoOpt.Core.Tests`: xUnit automated specification test suite (23 unit and benchmark tests).` |
| `docs/STEP1.md` | 9 | `TopoOpt` | `- **Core Geometry Components (`TopoOpt.Core.Geometry`)**:` |
| `docs/STEP1.md` | 14 | `TopoOpt` | `- **3D Viewport & Desktop UI (`TopoOpt.App`)**:` |
| `docs/STEP1.md` | 34 | `TopoOpt` | `### 1,001,112-Triangle Sphere Benchmark (`TopoOpt.Cli --benchmark-1m`)` |
| `docs/STEP1.md` | 44 | `TopoOpt` | `### 500,000-Triangle 3D Viewport Smoke Test (`TopoOpt.App --smoke-500k`)` |
| `docs/STEP2.md` | 5 | `TopoOpt` | `Step 2 delivers the structural discretisation and validation layer of **TopoOpt**, bridging geometric boundary representations (STL surface meshes) and voxel-based finite element methods (Hex8 structured grids).` |
| `docs/STEP3.md` | 5 | `TopoOpt` | `Step 3 delivers the complete Finite Element Method (FEM) simulation core of **TopoOpt**, located entirely in `TopoOpt.Core.Fem` with **zero UI dependencies**.` |
| `docs/STEP3.md` | 5 | `TopoOpt` | `Step 3 delivers the complete Finite Element Method (FEM) simulation core of **TopoOpt**, located entirely in `TopoOpt.Core.Fem` with **zero UI dependencies**.` |
| `docs/STEP3_BENCH.md` | 74 | `TopoOpt` | `- Because TopoOpt uses a regular structured Hex8 grid, geometric coarsening ($2 \times 2 \times 2$ cell agglomeration) is straightforward.` |
| `docs/STEP4.md` | 1 | `TopoOpt` | `# Step 4 — Headless Topology Optimisation in TopoOpt.Core & Dev CLI` |
| `docs/STEP4.md` | 5 | `TopoOpt` | `Step 4 delivers the complete headless topology optimization engine in `TopoOpt.Core.Optimization` and a lightweight developer CLI in `TopoOpt.Cli` (under 250 lines), with zero UI dependencies and full compliance with `AGENTS.md`.` |
| `docs/STEP4.md` | 5 | `TopoOpt` | `Step 4 delivers the complete headless topology optimization engine in `TopoOpt.Core.Optimization` and a lightweight developer CLI in `TopoOpt.Cli` (under 250 lines), with zero UI dependencies and full compliance with `AGENTS.md`.` |
| `docs/STEP4.md` | 33 | `TopoOpt` | `6. **Dev CLI (`TopoOpt.Cli`)**:` |
| `docs/STEP4.md` | 34 | `topoopt` | `- `topoopt analyze model.json`: Static analysis outputting compliance, max displacement, max von Mises stress.` |
| `docs/STEP4.md` | 35 | `topoopt` | `- `topoopt optimize model.json --out runs/x`: Optimization runner outputting progress per iteration, `metrics.csv`, and `density.bin` (raw float32 array).` |
| `docs/STEP4.md` | 127 | `TopoOpt` | `Point Jacobi PCG iteration counts scale with the longest dimension of slender structures. Future multigrid extensions (deferred per ADR-002) can be layered cleanly onto `TopoOpt.Core.Fem`.` |
| `docs/STEP5.md` | 5 | `TopoOpt` | `Step 5 delivers the complete interactive pre-processing and linear elastic FEA visualization pipeline for TopoOpt, bridging mesh import, boundary condition setup, voxel preparation, linear solve, and smooth 3D stress/displacement visualization on the original STL surface.` |
| `docs/STEP5.md` | 8 | `TopoOpt` | `1. **`WorkflowService` (`TopoOpt.Core.Workflow`)**:` |
| `docs/STEP5.md` | 21 | `TopoOpt` | `3. **Accordion Stepper UI (`TopoOpt.App`)**:` |
| `docs/STEP6.md` | 4 | `TopoOpt` | `Step 6 completes the interactive end-to-end topology optimisation workflow in `TopoOpt`:` |
| `docs/STEP7.md` | 1 | `TopoOpt` | `# TopoOpt Milestone 7: Production Release Readiness, Hardening, Performance & QA` |
| `docs/STEP7.md` | 4 | `TopoOpt` | `Milestone 7 completes the development roadmap of **TopoOpt**, taking the system from experimental prototype to hardened, release-ready production software for Linux and Windows. This milestone focuses strictly on non-functional requirements, input hardening, memory safety, deterministic concurrency, release packaging, and comprehensive QA verification:` |
| `docs/STEP7.md` | 10 | `TopoOpt` | `- Release archives packaged in `dist/TopoOpt-1.0.0-linux-x64.tar.gz` and `dist/TopoOpt-1.0.0-win-x64.zip`.` |
| `docs/STEP7.md` | 10 | `TopoOpt` | `- Release archives packaged in `dist/TopoOpt-1.0.0-linux-x64.tar.gz` and `dist/TopoOpt-1.0.0-win-x64.zip`.` |
| `docs/STEP7.md` | 14 | `TopoOpt` | `- **Crash Logger & Global Handlers**: Captures unhandled domain exceptions and unobserved task exceptions into `%LOCALAPPDATA%/TopoOpt/logs` (Windows) and `~/.local/share/TopoOpt/logs` (Linux).` |
| `docs/STEP7.md` | 14 | `TopoOpt` | `- **Crash Logger & Global Handlers**: Captures unhandled domain exceptions and unobserved task exceptions into `%LOCALAPPDATA%/TopoOpt/logs` (Windows) and `~/.local/share/TopoOpt/logs` (Linux).` |
| `docs/STEP7.md` | 32 | `TopoOpt` | `- **Linux Distribution**: `dist/TopoOpt-1.0.0-linux-x64.tar.gz`` |
| `docs/STEP7.md` | 36 | `TopoOpt` | `- **Windows Distribution**: `dist/TopoOpt-1.0.0-win-x64.zip`` |
| `docs/STEP7.md` | 37 | `TopoOpt` | `- Fully self-contained single-file Windows PE executable (`TopoOpt.exe`).` |
| `docs/STEP7.md` | 106 | `TopoOpt` | `2. **Linux Release Startup**: `dist/TopoOpt-1.0.0-linux-x64.tar.gz` extracted and verified on Wayland native and X11/XWayland with exit code 0 and successful UI initialization.` |
| `docs/STEP7.md` | ... | ... | *(and 3 more occurrences)* |
| `docs/STEP8_AUDIT.md` | 7 | `TopoOpt` | `1. **`UiResponsivenessMonitor`** (`src/TopoOpt.App/Diagnostics/UiResponsivenessMonitor.cs`):` |
| `docs/STEP8_AUDIT.md` | 13 | `TopoOpt` | `2. **`RenderLoopProbe`** (`src/TopoOpt.App/Diagnostics/RenderLoopProbe.cs`):` |
| `docs/STEP8_AUDIT.md` | 25 | `TopoOpt` | `4. **`ScenarioRunner` & Automated In-Window Test** (`src/TopoOpt.App/Diagnostics/ScenarioRunner.cs`):` |
| `docs/STEP8_AUDIT.md` | 35 | `TopoOpt` | `\| 1 \| **`.Result` / `.Wait()` / `GetAwaiter().GetResult()`** \| [`src/TopoOpt.Core/Optimization/OptimizationCoordinator.cs:308`](file:///run/media/simone/bdf9bcda-ba36-41a7-a41d-afdb4f87586c/Repos/StlOptimizer/src/TopoOpt.Core/Optimization/OptimizationCoordinator.cs#L308) \| **High** \| `channel.Writer.WriteAsync(snapshot, ...).AsTask().GetAwaiter().GetResult();` synchronously blocks the optimizer thread while pushing live snapshots. Must be replaced with non-blocking try-write or async dispatch. \|` |
| `docs/STEP8_AUDIT.md` | 35 | `TopoOpt` | `\| 1 \| **`.Result` / `.Wait()` / `GetAwaiter().GetResult()`** \| [`src/TopoOpt.Core/Optimization/OptimizationCoordinator.cs:308`](file:///run/media/simone/bdf9bcda-ba36-41a7-a41d-afdb4f87586c/Repos/StlOptimizer/src/TopoOpt.Core/Optimization/OptimizationCoordinator.cs#L308) \| **High** \| `channel.Writer.WriteAsync(snapshot, ...).AsTask().GetAwaiter().GetResult();` synchronously blocks the optimizer thread while pushing live snapshots. Must be replaced with non-blocking try-write or async dispatch. \|` |
| `docs/STEP8_AUDIT.md` | 37 | `TopoOpt` | `\| 3 \| **`async void` outside event handlers** \| [`src/TopoOpt.App/ViewModels/MainWindowViewModel.cs:1198`](file:///run/media/simone/bdf9bcda-ba36-41a7-a41d-afdb4f87586c/Repos/StlOptimizer/src/TopoOpt.App/ViewModels/MainWindowViewModel.cs#L1198)<br>[`src/TopoOpt.App/ViewModels/MainWindowViewModel.cs:1443`](file:///run/media/simone/bdf9bcda-ba36-41a7-a41d-afdb4f87586c/Repos/StlOptimizer/src/TopoOpt.App/ViewModels/MainWindowViewModel.cs#L1443) \| Low \| `OnAutosaveTick` and `OnLiveDisplayTimerTick` are `DispatcherTimer` tick handlers (`EventHandler`). To improve robustness, internal exceptions are trapped and logged. \|` |
| `docs/STEP8_AUDIT.md` | 37 | `TopoOpt` | `\| 3 \| **`async void` outside event handlers** \| [`src/TopoOpt.App/ViewModels/MainWindowViewModel.cs:1198`](file:///run/media/simone/bdf9bcda-ba36-41a7-a41d-afdb4f87586c/Repos/StlOptimizer/src/TopoOpt.App/ViewModels/MainWindowViewModel.cs#L1198)<br>[`src/TopoOpt.App/ViewModels/MainWindowViewModel.cs:1443`](file:///run/media/simone/bdf9bcda-ba36-41a7-a41d-afdb4f87586c/Repos/StlOptimizer/src/TopoOpt.App/ViewModels/MainWindowViewModel.cs#L1443) \| Low \| `OnAutosaveTick` and `OnLiveDisplayTimerTick` are `DispatcherTimer` tick handlers (`EventHandler`). To improve robustness, internal exceptions are trapped and logged. \|` |
| `docs/STEP8_AUDIT.md` | 37 | `TopoOpt` | `\| 3 \| **`async void` outside event handlers** \| [`src/TopoOpt.App/ViewModels/MainWindowViewModel.cs:1198`](file:///run/media/simone/bdf9bcda-ba36-41a7-a41d-afdb4f87586c/Repos/StlOptimizer/src/TopoOpt.App/ViewModels/MainWindowViewModel.cs#L1198)<br>[`src/TopoOpt.App/ViewModels/MainWindowViewModel.cs:1443`](file:///run/media/simone/bdf9bcda-ba36-41a7-a41d-afdb4f87586c/Repos/StlOptimizer/src/TopoOpt.App/ViewModels/MainWindowViewModel.cs#L1443) \| Low \| `OnAutosaveTick` and `OnLiveDisplayTimerTick` are `DispatcherTimer` tick handlers (`EventHandler`). To improve robustness, internal exceptions are trapped and logged. \|` |
| `docs/STEP8_AUDIT.md` | 37 | `TopoOpt` | `\| 3 \| **`async void` outside event handlers** \| [`src/TopoOpt.App/ViewModels/MainWindowViewModel.cs:1198`](file:///run/media/simone/bdf9bcda-ba36-41a7-a41d-afdb4f87586c/Repos/StlOptimizer/src/TopoOpt.App/ViewModels/MainWindowViewModel.cs#L1198)<br>[`src/TopoOpt.App/ViewModels/MainWindowViewModel.cs:1443`](file:///run/media/simone/bdf9bcda-ba36-41a7-a41d-afdb4f87586c/Repos/StlOptimizer/src/TopoOpt.App/ViewModels/MainWindowViewModel.cs#L1443) \| Low \| `OnAutosaveTick` and `OnLiveDisplayTimerTick` are `DispatcherTimer` tick handlers (`EventHandler`). To improve robustness, internal exceptions are trapped and logged. \|` |
| `docs/STEP8_AUDIT.md` | 38 | `TopoOpt` | `\| 4 \| **Heavy work in constructors, property setters, or bindings** \| [`src/TopoOpt.App/ViewModels/MainWindowViewModel.cs:756`](file:///run/media/simone/bdf9bcda-ba36-41a7-a41d-afdb4f87586c/Repos/StlOptimizer/src/TopoOpt.App/ViewModels/MainWindowViewModel.cs#L756)<br>[`src/TopoOpt.App/ViewModels/MainWindowViewModel.cs:767`](file:///run/media/simone/bdf9bcda-ba36-41a7-a41d-afdb4f87586c/Repos/StlOptimizer/src/TopoOpt.App/ViewModels/MainWindowViewModel.cs#L767) \| Medium \| Changing `SelectedResolutionPreset` or `TargetSolidElements` triggers `Workflow.SetResolution(...)` synchronously which invalidates prepared models and autosaves. \|` |
| `docs/STEP8_AUDIT.md` | ... | ... | *(and 17 more occurrences)* |
| `docs/USER_GUIDE.md` | 1 | `TopoOpt` | `# TopoOpt User Guide` |
| `docs/USER_GUIDE.md` | 3 | `TopoOpt` | `TopoOpt is a desktop application for 3D finite element analysis (FEA) and SIMP topology optimisation of parts intended for FDM/FFF 3D printing. All calculations use **millimetres (mm)**, **Newtons (N)**, and **Megapascals (MPa)** in a right-handed coordinate system with **Z up** (the print build direction).` |
| `docs/VALIDATION.md` | 3 | `TopoOpt` | `This document records the mathematical derivations, boundary condition formulations, analytical benchmark references, and numerical convergence results for the finite element solver in **TopoOpt.Core**.` |
| `docs/VALIDATION.md` | 39 | `TopoOpt` | `In TopoOpt, vectors are sized over the entire structured bounding box ($N_{\text{total}} = (N_x + 1)(N_y + 1)(N_z + 1)$ nodes, $3 N_{\text{total}}$ DOFs). Only active elements (where $\text{Domain}[e] = 1$) contribute to elasticity. Inactive nodes and prescribed support DOFs are constrained to zero.` |

### 12. Other C# Code / Test References
- **Total Matches**: 34 across 7 file(s)
| File Path | Line | Occurrence | Context |
|-----------|------|------------|---------|
| `src/TopoOpt.App/Diagnostics/ScenarioRunner.cs` | 28 | `TopoOpt` | `/// Programmatically executes the end-to-end TopoOpt workflow at the ViewModel layer` |
| `tests/TopoOpt.Core.Tests/App/GuideDialogTests.cs` | 15 | `TopoOpt` | `string candidate = Path.Combine(dir, "src", "TopoOpt.App", "Views", "MainWindow.axaml");` |
| `tests/TopoOpt.Core.Tests/App/GuideDialogTests.cs` | 52 | `TopoOpt` | `var load = new LoadItemViewModel(new TopoOpt.Core.Workflow.LoadRegion { Name = "Test Load" });` |
| `tests/TopoOpt.Core.Tests/App/StepperTests.cs` | 20 | `TopoOpt` | `string candidate = Path.Combine(dir, "src", "TopoOpt.App", "Views", "MainWindow.axaml");` |
| `tests/TopoOpt.Core.Tests/App/StepperTests.cs` | 112 | `TopoOpt` | `var load = new LoadItemViewModel(new TopoOpt.Core.Workflow.LoadRegion { Name = "Test Load" });` |
| `tests/TopoOpt.Core.Tests/App/StepperTests.cs` | 132 | `TopoOpt` | `var load = new LoadItemViewModel(new TopoOpt.Core.Workflow.LoadRegion { Name = "Test Load" });` |
| `tests/TopoOpt.Core.Tests/App/StepperTests.cs` | 153 | `TopoOpt` | `var load = new LoadItemViewModel(new TopoOpt.Core.Workflow.LoadRegion { Name = "Test Load" });` |
| `tests/TopoOpt.Core.Tests/Step6/Step6Tests.cs` | 126 | `TopoOpt` | `string tempDir = Path.Combine(Path.GetTempPath(), "TopoOpt_Step6_Export_" + Guid.NewGuid().ToString("N"));` |
| `tests/TopoOpt.Core.Tests/Step6/Step6Tests.cs` | 171 | `TopoOpt` | `string tempDir = Path.Combine(Path.GetTempPath(), "TopoOpt_Step6_Store_" + Guid.NewGuid().ToString("N"));` |
| `tests/TopoOpt.Core.Tests/Step6/Step6Tests.cs` | 321 | `TopoOpt` | `string tempDir = Path.Combine(Path.GetTempPath(), "TopoOpt_Step6_Backpressure_" + Guid.NewGuid().ToString("N"));` |
| `tests/TopoOpt.Core.Tests/Step6/Step6Tests.cs` | 422 | `TopoOpt` | `string tempDir = Path.Combine(Path.GetTempPath(), "TopoOpt_Step6_Resume_" + Guid.NewGuid().ToString("N"));` |
| `tests/TopoOpt.Core.Tests/Voxelization/SurfacePatchBuilderTests.cs` | 174 | `TopoOpt` | `float h = TopoOpt.Core.Workflow.ResolutionConfig.ComputeVoxelSize(mesh, 100_000);` |
| `tests/TopoOpt.Core.Tests/Workflow/WorkflowTests.cs` | 152 | `TopoOpt` | `string tempDir = Path.Combine(Path.GetTempPath(), "TopoOpt_TestProj_" + Guid.NewGuid().ToString("N"));` |
| `tools/generate_rename_plan.py` | 41 | `TopoOpt` | `elif rel_path.startswith('src/TopoOpt.Cli'):` |
| `tools/generate_rename_plan.py` | 57 | `TopoOpt` | `lines.append('# Product Rename Plan: TopoOpt → Mycel')` |
| `tools/generate_rename_plan.py` | 74 | `TopoOpt` | `lines.append('## 2. Inventory of TopoOpt Occurrences by Category')` |
| `tools/generate_rename_plan.py` | 83 | `TopoOpt` | `lines.append('\| **Directory** \| `src/TopoOpt.Core` \| `src/Mycel.Core` \|')` |
| `tools/generate_rename_plan.py` | 84 | `TopoOpt` | `lines.append('\| **Directory** \| `src/TopoOpt.App` \| `src/Mycel.App` \|')` |
| `tools/generate_rename_plan.py` | 85 | `TopoOpt` | `lines.append('\| **Directory** \| `src/TopoOpt.Cli` \| `src/Mycel.Cli` \|')` |
| `tools/generate_rename_plan.py` | 86 | `TopoOpt` | `lines.append('\| **Directory** \| `tests/TopoOpt.Core.Tests` \| `tests/Mycel.Core.Tests` \|')` |
| `tools/generate_rename_plan.py` | 87 | `TopoOpt` | `lines.append('\| **Solution** \| `TopoOpt.sln` \| `Mycel.sln` \|')` |
| `tools/generate_rename_plan.py` | 88 | `TopoOpt` | `lines.append('\| **Project** \| `src/TopoOpt.Core/TopoOpt.Core.csproj` \| `src/Mycel.Core/Mycel.Core.csproj` \|')` |
| `tools/generate_rename_plan.py` | 88 | `TopoOpt` | `lines.append('\| **Project** \| `src/TopoOpt.Core/TopoOpt.Core.csproj` \| `src/Mycel.Core/Mycel.Core.csproj` \|')` |
| `tools/generate_rename_plan.py` | ... | ... | *(and 11 more occurrences)* |

### 2. C# Namespaces and Using Directives
- **Total Matches**: 236 across 99 file(s)
| File Path | Line | Occurrence | Context |
|-----------|------|------------|---------|
| `src/TopoOpt.App/App.axaml.cs` | 4 | `TopoOpt` | `using TopoOpt.App.Views;` |
| `src/TopoOpt.App/App.axaml.cs` | 6 | `TopoOpt` | `namespace TopoOpt.App;` |
| `src/TopoOpt.App/Controls/ComplianceChartControl.cs` | 9 | `TopoOpt` | `namespace TopoOpt.App.Controls;` |
| `src/TopoOpt.App/Controls/Viewport3DControl.cs` | 14 | `TopoOpt` | `using TopoOpt.App.Diagnostics;` |
| `src/TopoOpt.App/Controls/Viewport3DControl.cs` | 15 | `TopoOpt` | `using TopoOpt.App.Rendering;` |
| `src/TopoOpt.App/Controls/Viewport3DControl.cs` | 16 | `TopoOpt` | `using TopoOpt.Core.Geometry;` |
| `src/TopoOpt.App/Controls/Viewport3DControl.cs` | 18 | `TopoOpt` | `namespace TopoOpt.App.Controls;` |
| `src/TopoOpt.App/Diagnostics/RenderLoopProbe.cs` | 4 | `TopoOpt` | `namespace TopoOpt.App.Diagnostics;` |
| `src/TopoOpt.App/Diagnostics/ScenarioRunner.cs` | 10 | `TopoOpt` | `using TopoOpt.App.ViewModels;` |
| `src/TopoOpt.App/Diagnostics/ScenarioRunner.cs` | 11 | `TopoOpt` | `using TopoOpt.Core.Geometry;` |
| `src/TopoOpt.App/Diagnostics/ScenarioRunner.cs` | 12 | `TopoOpt` | `using TopoOpt.Core.Workflow;` |
| `src/TopoOpt.App/Diagnostics/ScenarioRunner.cs` | 14 | `TopoOpt` | `namespace TopoOpt.App.Diagnostics;` |
| `src/TopoOpt.App/Diagnostics/UiResponsivenessMonitor.cs` | 6 | `TopoOpt` | `namespace TopoOpt.App.Diagnostics;` |
| `src/TopoOpt.App/Program.cs` | 3 | `TopoOpt` | `using TopoOpt.App.Diagnostics;` |
| `src/TopoOpt.App/Program.cs` | 5 | `TopoOpt` | `namespace TopoOpt.App;` |
| `src/TopoOpt.App/Rendering/ColorMap.cs` | 5 | `TopoOpt` | `namespace TopoOpt.App.Rendering;` |
| `src/TopoOpt.App/Rendering/LutColorMap.cs` | 5 | `TopoOpt` | `namespace TopoOpt.App.Rendering;` |
| `src/TopoOpt.App/Rendering/OrbitCamera.cs` | 3 | `TopoOpt` | `using TopoOpt.App.Diagnostics;` |
| `src/TopoOpt.App/Rendering/OrbitCamera.cs` | 5 | `TopoOpt` | `namespace TopoOpt.App.Rendering;` |
| `src/TopoOpt.App/ViewModels/LoadItemViewModel.cs` | 5 | `TopoOpt` | `using TopoOpt.Core.Workflow;` |
| `src/TopoOpt.App/ViewModels/LoadItemViewModel.cs` | 7 | `TopoOpt` | `namespace TopoOpt.App.ViewModels;` |
| `src/TopoOpt.App/ViewModels/MainWindowViewModel.cs` | 14 | `TopoOpt` | `using TopoOpt.App.Controls;` |
| `src/TopoOpt.App/ViewModels/MainWindowViewModel.cs` | 15 | `TopoOpt` | `using TopoOpt.App.Diagnostics;` |
| `src/TopoOpt.App/ViewModels/MainWindowViewModel.cs` | 16 | `TopoOpt` | `using TopoOpt.App.Rendering;` |
| `src/TopoOpt.App/ViewModels/MainWindowViewModel.cs` | 17 | `TopoOpt` | `using TopoOpt.Core.Fem;` |
| `src/TopoOpt.App/ViewModels/MainWindowViewModel.cs` | 18 | `TopoOpt` | `using TopoOpt.Core.Geometry;` |
| `src/TopoOpt.App/ViewModels/MainWindowViewModel.cs` | 19 | `TopoOpt` | `using TopoOpt.Core.Optimization;` |
| `src/TopoOpt.App/ViewModels/MainWindowViewModel.cs` | 20 | `TopoOpt` | `using TopoOpt.Core.Persistence;` |
| `src/TopoOpt.App/ViewModels/MainWindowViewModel.cs` | 21 | `TopoOpt` | `using TopoOpt.Core.PostProcessing;` |
| `src/TopoOpt.App/ViewModels/MainWindowViewModel.cs` | 22 | `TopoOpt` | `using TopoOpt.Core.Voxelization;` |
| `src/TopoOpt.App/ViewModels/MainWindowViewModel.cs` | 23 | `TopoOpt` | `using TopoOpt.Core.Workflow;` |
| `src/TopoOpt.App/ViewModels/MainWindowViewModel.cs` | ... | ... | *(and 1 more occurrences)* |
| `src/TopoOpt.App/ViewModels/PreserveItemViewModel.cs` | 4 | `TopoOpt` | `using TopoOpt.Core.Workflow;` |
| `src/TopoOpt.App/ViewModels/PreserveItemViewModel.cs` | 6 | `TopoOpt` | `namespace TopoOpt.App.ViewModels;` |
| `src/TopoOpt.App/ViewModels/SelectionItemViewModel.cs` | 4 | `TopoOpt` | `using TopoOpt.Core.Geometry;` |
| `src/TopoOpt.App/ViewModels/SelectionItemViewModel.cs` | 6 | `TopoOpt` | `namespace TopoOpt.App.ViewModels;` |
| `src/TopoOpt.App/ViewModels/SupportItemViewModel.cs` | 4 | `TopoOpt` | `using TopoOpt.Core.Workflow;` |
| `src/TopoOpt.App/ViewModels/SupportItemViewModel.cs` | 6 | `TopoOpt` | `namespace TopoOpt.App.ViewModels;` |
| `src/TopoOpt.App/Views/MainWindow.axaml.cs` | 13 | `TopoOpt` | `using TopoOpt.App.Diagnostics;` |
| `src/TopoOpt.App/Views/MainWindow.axaml.cs` | 14 | `TopoOpt` | `using TopoOpt.App.ViewModels;` |
| `src/TopoOpt.App/Views/MainWindow.axaml.cs` | 15 | `TopoOpt` | `using TopoOpt.Core.Geometry;` |
| `src/TopoOpt.App/Views/MainWindow.axaml.cs` | 17 | `TopoOpt` | `namespace TopoOpt.App.Views;` |
| `src/TopoOpt.Core/Diagnostics/MemoryGuard.cs` | 5 | `TopoOpt` | `namespace TopoOpt.Core.Diagnostics;` |
| `src/TopoOpt.Core/Fem/FeMaterial.cs` | 3 | `TopoOpt` | `namespace TopoOpt.Core.Fem;` |
| `src/TopoOpt.Core/Fem/FeModel.cs` | 4 | `TopoOpt` | `using TopoOpt.Core.Voxelization;` |
| `src/TopoOpt.Core/Fem/FeModel.cs` | 6 | `TopoOpt` | `namespace TopoOpt.Core.Fem;` |
| `src/TopoOpt.Core/Fem/FemOperator.cs` | 7 | `TopoOpt` | `namespace TopoOpt.Core.Fem;` |
| `src/TopoOpt.Core/Fem/FemWorkTeam.cs` | 4 | `TopoOpt` | `namespace TopoOpt.Core.Fem;` |
| `src/TopoOpt.Core/Fem/Hex8Element.cs` | 3 | `TopoOpt` | `namespace TopoOpt.Core.Fem;` |
| `src/TopoOpt.Core/Fem/IPreconditioner.cs` | 3 | `TopoOpt` | `namespace TopoOpt.Core.Fem;` |
| `src/TopoOpt.Core/Fem/JacobiPreconditioner.cs` | 3 | `TopoOpt` | `namespace TopoOpt.Core.Fem;` |
| `src/TopoOpt.Core/Fem/LoadCase.cs` | 3 | `TopoOpt` | `using TopoOpt.Core.Voxelization;` |
| `src/TopoOpt.Core/Fem/LoadCase.cs` | 5 | `TopoOpt` | `namespace TopoOpt.Core.Fem;` |
| `src/TopoOpt.Core/Fem/PcgResult.cs` | 3 | `TopoOpt` | `namespace TopoOpt.Core.Fem;` |
| `src/TopoOpt.Core/Fem/PcgSolver.cs` | 8 | `TopoOpt` | `namespace TopoOpt.Core.Fem;` |
| `src/TopoOpt.Core/Fem/StaticAnalysis.cs` | 6 | `TopoOpt` | `namespace TopoOpt.Core.Fem;` |
| `src/TopoOpt.Core/Geometry/Bvh.cs` | 7 | `TopoOpt` | `namespace TopoOpt.Core.Geometry;` |
| `src/TopoOpt.Core/Geometry/IndexedMesh.cs` | 4 | `TopoOpt` | `namespace TopoOpt.Core.Geometry;` |
| `src/TopoOpt.Core/Geometry/LengthUnit.cs` | 1 | `TopoOpt` | `namespace TopoOpt.Core.Geometry;` |
| `src/TopoOpt.Core/Geometry/MeshReport.cs` | 4 | `TopoOpt` | `namespace TopoOpt.Core.Geometry;` |
| `src/TopoOpt.Core/Geometry/RegionSelector.cs` | 7 | `TopoOpt` | `namespace TopoOpt.Core.Geometry;` |
| `src/TopoOpt.Core/Geometry/StlReader.cs` | 13 | `TopoOpt` | `namespace TopoOpt.Core.Geometry;` |
| `src/TopoOpt.Core/Geometry/SurfaceSelection.cs` | 3 | `TopoOpt` | `namespace TopoOpt.Core.Geometry;` |
| `src/TopoOpt.Core/Optimization/DensityFilter.cs` | 4 | `TopoOpt` | `using TopoOpt.Core.Voxelization;` |
| `src/TopoOpt.Core/Optimization/DensityFilter.cs` | 6 | `TopoOpt` | `namespace TopoOpt.Core.Optimization;` |
| `src/TopoOpt.Core/Optimization/HeavisideProjection.cs` | 3 | `TopoOpt` | `namespace TopoOpt.Core.Optimization;` |
| `src/TopoOpt.Core/Optimization/IterationController.cs` | 4 | `TopoOpt` | `namespace TopoOpt.Core.Optimization;` |
| `src/TopoOpt.Core/Optimization/IterationSnapshot.cs` | 3 | `TopoOpt` | `namespace TopoOpt.Core.Optimization;` |
| `src/TopoOpt.Core/Optimization/OcOptimizer.cs` | 3 | `TopoOpt` | `namespace TopoOpt.Core.Optimization;` |
| `src/TopoOpt.Core/Optimization/OptimizationCoordinator.cs` | 8 | `TopoOpt` | `using TopoOpt.Core.Fem;` |
| `src/TopoOpt.Core/Optimization/OptimizationCoordinator.cs` | 9 | `TopoOpt` | `using TopoOpt.Core.Geometry;` |
| `src/TopoOpt.Core/Optimization/OptimizationCoordinator.cs` | 10 | `TopoOpt` | `using TopoOpt.Core.Persistence;` |
| `src/TopoOpt.Core/Optimization/OptimizationCoordinator.cs` | 11 | `TopoOpt` | `using TopoOpt.Core.PostProcessing;` |
| `src/TopoOpt.Core/Optimization/OptimizationCoordinator.cs` | 12 | `TopoOpt` | `using TopoOpt.Core.Voxelization;` |
| `src/TopoOpt.Core/Optimization/OptimizationCoordinator.cs` | 14 | `TopoOpt` | `namespace TopoOpt.Core.Optimization;` |
| `src/TopoOpt.Core/Optimization/OptimizerOptions.cs` | 3 | `TopoOpt` | `namespace TopoOpt.Core.Optimization;` |
| `src/TopoOpt.Core/Optimization/SensitivityAnalysis.cs` | 3 | `TopoOpt` | `using TopoOpt.Core.Fem;` |
| `src/TopoOpt.Core/Optimization/SensitivityAnalysis.cs` | 5 | `TopoOpt` | `namespace TopoOpt.Core.Optimization;` |
| `src/TopoOpt.Core/Optimization/TopologyOptimizationResult.cs` | 4 | `TopoOpt` | `namespace TopoOpt.Core.Optimization;` |
| `src/TopoOpt.Core/Optimization/TopologyOptimizer.cs` | 5 | `TopoOpt` | `using TopoOpt.Core.Fem;` |
| `src/TopoOpt.Core/Optimization/TopologyOptimizer.cs` | 6 | `TopoOpt` | `using TopoOpt.Core.Voxelization;` |
| `src/TopoOpt.Core/Optimization/TopologyOptimizer.cs` | 8 | `TopoOpt` | `namespace TopoOpt.Core.Optimization;` |
| `src/TopoOpt.Core/Persistence/RunModels.cs` | 5 | `TopoOpt` | `using TopoOpt.Core.Geometry;` |
| `src/TopoOpt.Core/Persistence/RunModels.cs` | 6 | `TopoOpt` | `using TopoOpt.Core.Voxelization;` |
| `src/TopoOpt.Core/Persistence/RunModels.cs` | 7 | `TopoOpt` | `using TopoOpt.Core.Workflow;` |
| `src/TopoOpt.Core/Persistence/RunModels.cs` | 9 | `TopoOpt` | `namespace TopoOpt.Core.Persistence;` |
| `src/TopoOpt.Core/Persistence/RunReader.cs` | 11 | `TopoOpt` | `using TopoOpt.Core.Geometry;` |
| `src/TopoOpt.Core/Persistence/RunReader.cs` | 12 | `TopoOpt` | `using TopoOpt.Core.PostProcessing;` |
| `src/TopoOpt.Core/Persistence/RunReader.cs` | 13 | `TopoOpt` | `using TopoOpt.Core.Voxelization;` |
| `src/TopoOpt.Core/Persistence/RunReader.cs` | 15 | `TopoOpt` | `namespace TopoOpt.Core.Persistence;` |
| `src/TopoOpt.Core/Persistence/RunWriter.cs` | 11 | `TopoOpt` | `using TopoOpt.Core.Optimization;` |
| `src/TopoOpt.Core/Persistence/RunWriter.cs` | 13 | `TopoOpt` | `namespace TopoOpt.Core.Persistence;` |
| `src/TopoOpt.Core/PostProcessing/SurfaceNetsExtractor.cs` | 6 | `TopoOpt` | `using TopoOpt.Core.Geometry;` |
| `src/TopoOpt.Core/PostProcessing/SurfaceNetsExtractor.cs` | 7 | `TopoOpt` | `using TopoOpt.Core.Voxelization;` |
| `src/TopoOpt.Core/PostProcessing/SurfaceNetsExtractor.cs` | 9 | `TopoOpt` | `namespace TopoOpt.Core.PostProcessing;` |
| `src/TopoOpt.Core/PostProcessing/TaubinSmoother.cs` | 5 | `TopoOpt` | `using TopoOpt.Core.Geometry;` |
| `src/TopoOpt.Core/PostProcessing/TaubinSmoother.cs` | 7 | `TopoOpt` | `namespace TopoOpt.Core.PostProcessing;` |
| `src/TopoOpt.Core/Voxelization/Connectivity.cs` | 4 | `TopoOpt` | `namespace TopoOpt.Core.Voxelization;` |
| `src/TopoOpt.Core/Voxelization/GridSpec.cs` | 3 | `TopoOpt` | `using TopoOpt.Core.Geometry;` |
| `src/TopoOpt.Core/Voxelization/GridSpec.cs` | 5 | `TopoOpt` | `namespace TopoOpt.Core.Voxelization;` |
| `src/TopoOpt.Core/Voxelization/ModelValidator.cs` | 5 | `TopoOpt` | `using TopoOpt.Core.Geometry;` |
| `src/TopoOpt.Core/Voxelization/ModelValidator.cs` | 7 | `TopoOpt` | `namespace TopoOpt.Core.Voxelization;` |
| `src/TopoOpt.Core/Voxelization/PassiveRegion.cs` | 4 | `TopoOpt` | `namespace TopoOpt.Core.Voxelization;` |
| `src/TopoOpt.Core/Voxelization/SurfacePatch.cs` | 5 | `TopoOpt` | `namespace TopoOpt.Core.Voxelization;` |
| `src/TopoOpt.Core/Voxelization/SurfacePatchBuilder.cs` | 5 | `TopoOpt` | `using TopoOpt.Core.Geometry;` |
| `src/TopoOpt.Core/Voxelization/SurfacePatchBuilder.cs` | 7 | `TopoOpt` | `namespace TopoOpt.Core.Voxelization;` |
| `src/TopoOpt.Core/Voxelization/ValidationIssue.cs` | 3 | `TopoOpt` | `namespace TopoOpt.Core.Voxelization;` |
| `src/TopoOpt.Core/Voxelization/VoxelMeshBuilder.cs` | 4 | `TopoOpt` | `using TopoOpt.Core.Geometry;` |
| `src/TopoOpt.Core/Voxelization/VoxelMeshBuilder.cs` | 6 | `TopoOpt` | `namespace TopoOpt.Core.Voxelization;` |
| `src/TopoOpt.Core/Voxelization/VoxelModel.cs` | 4 | `TopoOpt` | `namespace TopoOpt.Core.Voxelization;` |
| `src/TopoOpt.Core/Voxelization/Voxeliser.cs` | 6 | `TopoOpt` | `using TopoOpt.Core.Geometry;` |
| `src/TopoOpt.Core/Voxelization/Voxeliser.cs` | 8 | `TopoOpt` | `namespace TopoOpt.Core.Voxelization;` |
| `src/TopoOpt.Core/Workflow/MaterialPresets.cs` | 3 | `TopoOpt` | `namespace TopoOpt.Core.Workflow;` |
| `src/TopoOpt.Core/Workflow/PreparedModel.cs` | 3 | `TopoOpt` | `using TopoOpt.Core.Fem;` |
| `src/TopoOpt.Core/Workflow/PreparedModel.cs` | 4 | `TopoOpt` | `using TopoOpt.Core.Voxelization;` |
| `src/TopoOpt.Core/Workflow/PreparedModel.cs` | 6 | `TopoOpt` | `namespace TopoOpt.Core.Workflow;` |
| `src/TopoOpt.Core/Workflow/ProjectSerializer.cs` | 8 | `TopoOpt` | `using TopoOpt.Core.Geometry;` |
| `src/TopoOpt.Core/Workflow/ProjectSerializer.cs` | 10 | `TopoOpt` | `namespace TopoOpt.Core.Workflow;` |
| `src/TopoOpt.Core/Workflow/ProjectState.cs` | 3 | `TopoOpt` | `using TopoOpt.Core.Geometry;` |
| `src/TopoOpt.Core/Workflow/ProjectState.cs` | 5 | `TopoOpt` | `namespace TopoOpt.Core.Workflow;` |
| `src/TopoOpt.Core/Workflow/RegionDefinitions.cs` | 4 | `TopoOpt` | `using TopoOpt.Core.Geometry;` |
| `src/TopoOpt.Core/Workflow/RegionDefinitions.cs` | 6 | `TopoOpt` | `namespace TopoOpt.Core.Workflow;` |
| `src/TopoOpt.Core/Workflow/ResolutionConfig.cs` | 3 | `TopoOpt` | `using TopoOpt.Core.Geometry;` |
| `src/TopoOpt.Core/Workflow/ResolutionConfig.cs` | 5 | `TopoOpt` | `namespace TopoOpt.Core.Workflow;` |
| `src/TopoOpt.Core/Workflow/WorkflowAnalysisResult.cs` | 3 | `TopoOpt` | `namespace TopoOpt.Core.Workflow;` |
| `src/TopoOpt.Core/Workflow/WorkflowService.cs` | 10 | `TopoOpt` | `using TopoOpt.Core.Diagnostics;` |
| `src/TopoOpt.Core/Workflow/WorkflowService.cs` | 11 | `TopoOpt` | `using TopoOpt.Core.Fem;` |
| `src/TopoOpt.Core/Workflow/WorkflowService.cs` | 12 | `TopoOpt` | `using TopoOpt.Core.Geometry;` |
| `src/TopoOpt.Core/Workflow/WorkflowService.cs` | 13 | `TopoOpt` | `using TopoOpt.Core.Optimization;` |
| `src/TopoOpt.Core/Workflow/WorkflowService.cs` | 14 | `TopoOpt` | `using TopoOpt.Core.Persistence;` |
| `src/TopoOpt.Core/Workflow/WorkflowService.cs` | 15 | `TopoOpt` | `using TopoOpt.Core.PostProcessing;` |
| `src/TopoOpt.Core/Workflow/WorkflowService.cs` | 16 | `TopoOpt` | `using TopoOpt.Core.Voxelization;` |
| `src/TopoOpt.Core/Workflow/WorkflowService.cs` | 18 | `TopoOpt` | `namespace TopoOpt.Core.Workflow;` |
| `tests/TopoOpt.Core.Tests/App/GuideDialogTests.cs` | 3 | `TopoOpt` | `using TopoOpt.App.ViewModels;` |
| `tests/TopoOpt.Core.Tests/App/GuideDialogTests.cs` | 6 | `TopoOpt` | `namespace TopoOpt.Core.Tests.App;` |
| `tests/TopoOpt.Core.Tests/App/LoadItemViewModelTests.cs` | 2 | `TopoOpt` | `using TopoOpt.App.ViewModels;` |
| `tests/TopoOpt.Core.Tests/App/LoadItemViewModelTests.cs` | 3 | `TopoOpt` | `using TopoOpt.Core.Workflow;` |
| `tests/TopoOpt.Core.Tests/App/LoadItemViewModelTests.cs` | 6 | `TopoOpt` | `namespace TopoOpt.Core.Tests.App;` |
| `tests/TopoOpt.Core.Tests/App/PickingAngleAndDimensionsTests.cs` | 4 | `TopoOpt` | `using TopoOpt.App.ViewModels;` |
| `tests/TopoOpt.Core.Tests/App/PickingAngleAndDimensionsTests.cs` | 5 | `TopoOpt` | `using TopoOpt.Core.Geometry;` |
| `tests/TopoOpt.Core.Tests/App/PickingAngleAndDimensionsTests.cs` | 6 | `TopoOpt` | `using TopoOpt.Core.Tests.Fixtures;` |
| `tests/TopoOpt.Core.Tests/App/PickingAngleAndDimensionsTests.cs` | 7 | `TopoOpt` | `using TopoOpt.Core.Workflow;` |
| `tests/TopoOpt.Core.Tests/App/PickingAngleAndDimensionsTests.cs` | 10 | `TopoOpt` | `namespace TopoOpt.Core.Tests.App;` |
| `tests/TopoOpt.Core.Tests/App/StepperTests.cs` | 4 | `TopoOpt` | `using TopoOpt.App.ViewModels;` |
| `tests/TopoOpt.Core.Tests/App/StepperTests.cs` | 7 | `TopoOpt` | `namespace TopoOpt.Core.Tests.App;` |
| `tests/TopoOpt.Core.Tests/Diagnostics/MemoryGuardTests.cs` | 2 | `TopoOpt` | `using TopoOpt.Core.Diagnostics;` |
| `tests/TopoOpt.Core.Tests/Diagnostics/MemoryGuardTests.cs` | 5 | `TopoOpt` | `namespace TopoOpt.Core.Tests.Diagnostics;` |
| `tests/TopoOpt.Core.Tests/Fem/FeModelTests.cs` | 4 | `TopoOpt` | `using TopoOpt.Core.Fem;` |
| `tests/TopoOpt.Core.Tests/Fem/FeModelTests.cs` | 5 | `TopoOpt` | `using TopoOpt.Core.Voxelization;` |
| `tests/TopoOpt.Core.Tests/Fem/FeModelTests.cs` | 8 | `TopoOpt` | `namespace TopoOpt.Core.Tests.Fem;` |
| `tests/TopoOpt.Core.Tests/Fem/FemValidationTests.cs` | 5 | `TopoOpt` | `using TopoOpt.Core.Fem;` |
| `tests/TopoOpt.Core.Tests/Fem/FemValidationTests.cs` | 6 | `TopoOpt` | `using TopoOpt.Core.Voxelization;` |
| `tests/TopoOpt.Core.Tests/Fem/FemValidationTests.cs` | 9 | `TopoOpt` | `namespace TopoOpt.Core.Tests.Fem;` |
| `tests/TopoOpt.Core.Tests/Fem/Hex8ElementTests.cs` | 2 | `TopoOpt` | `using TopoOpt.Core.Fem;` |
| `tests/TopoOpt.Core.Tests/Fem/Hex8ElementTests.cs` | 5 | `TopoOpt` | `namespace TopoOpt.Core.Tests.Fem;` |
| `tests/TopoOpt.Core.Tests/Fem/StaticAnalysisTests.cs` | 3 | `TopoOpt` | `using TopoOpt.Core.Fem;` |
| `tests/TopoOpt.Core.Tests/Fem/StaticAnalysisTests.cs` | 4 | `TopoOpt` | `using TopoOpt.Core.Voxelization;` |
| `tests/TopoOpt.Core.Tests/Fem/StaticAnalysisTests.cs` | 7 | `TopoOpt` | `namespace TopoOpt.Core.Tests.Fem;` |
| `tests/TopoOpt.Core.Tests/Fixtures/StlFixtures.cs` | 10 | `TopoOpt` | `namespace TopoOpt.Core.Tests.Fixtures;` |
| `tests/TopoOpt.Core.Tests/Fixtures/StlFixturesTests.cs` | 9 | `TopoOpt` | `namespace TopoOpt.Core.Tests.Fixtures;` |
| `tests/TopoOpt.Core.Tests/Geometry/BvhTests.cs` | 5 | `TopoOpt` | `using TopoOpt.Core.Geometry;` |
| `tests/TopoOpt.Core.Tests/Geometry/BvhTests.cs` | 6 | `TopoOpt` | `using TopoOpt.Core.Tests.Fixtures;` |
| `tests/TopoOpt.Core.Tests/Geometry/BvhTests.cs` | 9 | `TopoOpt` | `namespace TopoOpt.Core.Tests.Geometry;` |
| `tests/TopoOpt.Core.Tests/Geometry/OrbitCameraTests.cs` | 5 | `TopoOpt` | `using TopoOpt.App.Controls;` |
| `tests/TopoOpt.Core.Tests/Geometry/OrbitCameraTests.cs` | 6 | `TopoOpt` | `using TopoOpt.App.Rendering;` |
| `tests/TopoOpt.Core.Tests/Geometry/OrbitCameraTests.cs` | 9 | `TopoOpt` | `namespace TopoOpt.Core.Tests.Geometry;` |
| `tests/TopoOpt.Core.Tests/Geometry/PerformanceSmokeTests.cs` | 6 | `TopoOpt` | `using TopoOpt.Core.Geometry;` |
| `tests/TopoOpt.Core.Tests/Geometry/PerformanceSmokeTests.cs` | 7 | `TopoOpt` | `using TopoOpt.Core.Tests.Fixtures;` |
| `tests/TopoOpt.Core.Tests/Geometry/PerformanceSmokeTests.cs` | 11 | `TopoOpt` | `namespace TopoOpt.Core.Tests.Geometry;` |
| `tests/TopoOpt.Core.Tests/Geometry/RegionSelectorTests.cs` | 6 | `TopoOpt` | `using TopoOpt.Core.Geometry;` |
| `tests/TopoOpt.Core.Tests/Geometry/RegionSelectorTests.cs` | 7 | `TopoOpt` | `using TopoOpt.Core.Tests.Fixtures;` |
| `tests/TopoOpt.Core.Tests/Geometry/RegionSelectorTests.cs` | 10 | `TopoOpt` | `namespace TopoOpt.Core.Tests.Geometry;` |
| `tests/TopoOpt.Core.Tests/Geometry/StlHardeningTests.cs` | 6 | `TopoOpt` | `using TopoOpt.Core.Geometry;` |
| `tests/TopoOpt.Core.Tests/Geometry/StlHardeningTests.cs` | 9 | `TopoOpt` | `namespace TopoOpt.Core.Tests.Geometry;` |
| `tests/TopoOpt.Core.Tests/Geometry/StlReaderWriterTests.cs` | 6 | `TopoOpt` | `using TopoOpt.Core.Geometry;` |
| `tests/TopoOpt.Core.Tests/Geometry/StlReaderWriterTests.cs` | 7 | `TopoOpt` | `using TopoOpt.Core.Tests.Fixtures;` |
| `tests/TopoOpt.Core.Tests/Geometry/StlReaderWriterTests.cs` | 10 | `TopoOpt` | `namespace TopoOpt.Core.Tests.Geometry;` |
| `tests/TopoOpt.Core.Tests/Optimization/OptimizationTests.cs` | 9 | `TopoOpt` | `using TopoOpt.Core.Fem;` |
| `tests/TopoOpt.Core.Tests/Optimization/OptimizationTests.cs` | 10 | `TopoOpt` | `using TopoOpt.Core.Optimization;` |
| `tests/TopoOpt.Core.Tests/Optimization/OptimizationTests.cs` | 11 | `TopoOpt` | `using TopoOpt.Core.Voxelization;` |
| `tests/TopoOpt.Core.Tests/Optimization/OptimizationTests.cs` | 14 | `TopoOpt` | `namespace TopoOpt.Core.Tests.Optimization;` |
| `tests/TopoOpt.Core.Tests/Optimization/PerformanceProfilingTests.cs` | 5 | `TopoOpt` | `using TopoOpt.Core.Fem;` |
| `tests/TopoOpt.Core.Tests/Optimization/PerformanceProfilingTests.cs` | 6 | `TopoOpt` | `using TopoOpt.Core.Optimization;` |
| `tests/TopoOpt.Core.Tests/Optimization/PerformanceProfilingTests.cs` | 7 | `TopoOpt` | `using TopoOpt.Core.Voxelization;` |
| `tests/TopoOpt.Core.Tests/Optimization/PerformanceProfilingTests.cs` | 11 | `TopoOpt` | `namespace TopoOpt.Core.Tests.Optimization;` |
| `tests/TopoOpt.Core.Tests/Optimization/UiResponsivenessTests.cs` | 7 | `TopoOpt` | `using TopoOpt.Core.Fem;` |
| `tests/TopoOpt.Core.Tests/Optimization/UiResponsivenessTests.cs` | 8 | `TopoOpt` | `using TopoOpt.Core.Optimization;` |
| `tests/TopoOpt.Core.Tests/Optimization/UiResponsivenessTests.cs` | 9 | `TopoOpt` | `using TopoOpt.Core.Voxelization;` |
| `tests/TopoOpt.Core.Tests/Optimization/UiResponsivenessTests.cs` | 13 | `TopoOpt` | `namespace TopoOpt.Core.Tests.Optimization;` |
| `tests/TopoOpt.Core.Tests/Step6/Step6Tests.cs` | 10 | `TopoOpt` | `using TopoOpt.Core.Fem;` |
| `tests/TopoOpt.Core.Tests/Step6/Step6Tests.cs` | 11 | `TopoOpt` | `using TopoOpt.Core.Geometry;` |
| `tests/TopoOpt.Core.Tests/Step6/Step6Tests.cs` | 12 | `TopoOpt` | `using TopoOpt.Core.Optimization;` |
| `tests/TopoOpt.Core.Tests/Step6/Step6Tests.cs` | 13 | `TopoOpt` | `using TopoOpt.Core.Persistence;` |
| `tests/TopoOpt.Core.Tests/Step6/Step6Tests.cs` | 14 | `TopoOpt` | `using TopoOpt.Core.PostProcessing;` |
| `tests/TopoOpt.Core.Tests/Step6/Step6Tests.cs` | 15 | `TopoOpt` | `using TopoOpt.Core.Voxelization;` |
| `tests/TopoOpt.Core.Tests/Step6/Step6Tests.cs` | 16 | `TopoOpt` | `using TopoOpt.Core.Workflow;` |
| `tests/TopoOpt.Core.Tests/Step6/Step6Tests.cs` | 19 | `TopoOpt` | `namespace TopoOpt.Core.Tests.Step6;` |
| `tests/TopoOpt.Core.Tests/Voxelization/ConnectivityTests.cs` | 2 | `TopoOpt` | `using TopoOpt.Core.Voxelization;` |
| `tests/TopoOpt.Core.Tests/Voxelization/ConnectivityTests.cs` | 5 | `TopoOpt` | `namespace TopoOpt.Core.Tests.Voxelization;` |
| `tests/TopoOpt.Core.Tests/Voxelization/GridSpecTests.cs` | 3 | `TopoOpt` | `using TopoOpt.Core.Geometry;` |
| `tests/TopoOpt.Core.Tests/Voxelization/GridSpecTests.cs` | 4 | `TopoOpt` | `using TopoOpt.Core.Tests.Fixtures;` |
| `tests/TopoOpt.Core.Tests/Voxelization/GridSpecTests.cs` | 5 | `TopoOpt` | `using TopoOpt.Core.Voxelization;` |
| `tests/TopoOpt.Core.Tests/Voxelization/GridSpecTests.cs` | 8 | `TopoOpt` | `namespace TopoOpt.Core.Tests.Voxelization;` |
| `tests/TopoOpt.Core.Tests/Voxelization/ModelValidatorTests.cs` | 5 | `TopoOpt` | `using TopoOpt.Core.Geometry;` |
| `tests/TopoOpt.Core.Tests/Voxelization/ModelValidatorTests.cs` | 6 | `TopoOpt` | `using TopoOpt.Core.Tests.Fixtures;` |
| `tests/TopoOpt.Core.Tests/Voxelization/ModelValidatorTests.cs` | 7 | `TopoOpt` | `using TopoOpt.Core.Voxelization;` |
| `tests/TopoOpt.Core.Tests/Voxelization/ModelValidatorTests.cs` | 10 | `TopoOpt` | `namespace TopoOpt.Core.Tests.Voxelization;` |
| `tests/TopoOpt.Core.Tests/Voxelization/PassiveRegionTests.cs` | 4 | `TopoOpt` | `using TopoOpt.Core.Geometry;` |
| `tests/TopoOpt.Core.Tests/Voxelization/PassiveRegionTests.cs` | 5 | `TopoOpt` | `using TopoOpt.Core.Tests.Fixtures;` |
| `tests/TopoOpt.Core.Tests/Voxelization/PassiveRegionTests.cs` | 6 | `TopoOpt` | `using TopoOpt.Core.Voxelization;` |
| `tests/TopoOpt.Core.Tests/Voxelization/PassiveRegionTests.cs` | 9 | `TopoOpt` | `namespace TopoOpt.Core.Tests.Voxelization;` |
| `tests/TopoOpt.Core.Tests/Voxelization/SurfacePatchBuilderTests.cs` | 6 | `TopoOpt` | `using TopoOpt.Core.Geometry;` |
| `tests/TopoOpt.Core.Tests/Voxelization/SurfacePatchBuilderTests.cs` | 7 | `TopoOpt` | `using TopoOpt.Core.Tests.Fixtures;` |
| `tests/TopoOpt.Core.Tests/Voxelization/SurfacePatchBuilderTests.cs` | 8 | `TopoOpt` | `using TopoOpt.Core.Voxelization;` |
| `tests/TopoOpt.Core.Tests/Voxelization/SurfacePatchBuilderTests.cs` | 11 | `TopoOpt` | `namespace TopoOpt.Core.Tests.Voxelization;` |
| `tests/TopoOpt.Core.Tests/Voxelization/VoxelBenchmarkTests.cs` | 5 | `TopoOpt` | `using TopoOpt.Core.Geometry;` |
| `tests/TopoOpt.Core.Tests/Voxelization/VoxelBenchmarkTests.cs` | 6 | `TopoOpt` | `using TopoOpt.Core.Tests.Fixtures;` |
| `tests/TopoOpt.Core.Tests/Voxelization/VoxelBenchmarkTests.cs` | 7 | `TopoOpt` | `using TopoOpt.Core.Voxelization;` |
| `tests/TopoOpt.Core.Tests/Voxelization/VoxelBenchmarkTests.cs` | 11 | `TopoOpt` | `namespace TopoOpt.Core.Tests.Voxelization;` |
| `tests/TopoOpt.Core.Tests/Voxelization/VoxeliserTests.cs` | 6 | `TopoOpt` | `using TopoOpt.Core.Geometry;` |
| `tests/TopoOpt.Core.Tests/Voxelization/VoxeliserTests.cs` | 7 | `TopoOpt` | `using TopoOpt.Core.Tests.Fixtures;` |
| `tests/TopoOpt.Core.Tests/Voxelization/VoxeliserTests.cs` | 8 | `TopoOpt` | `using TopoOpt.Core.Voxelization;` |
| `tests/TopoOpt.Core.Tests/Voxelization/VoxeliserTests.cs` | 11 | `TopoOpt` | `namespace TopoOpt.Core.Tests.Voxelization;` |
| `tests/TopoOpt.Core.Tests/Workflow/InfillAndFilamentTests.cs` | 4 | `TopoOpt` | `using TopoOpt.App.ViewModels;` |
| `tests/TopoOpt.Core.Tests/Workflow/InfillAndFilamentTests.cs` | 5 | `TopoOpt` | `using TopoOpt.Core.Geometry;` |
| `tests/TopoOpt.Core.Tests/Workflow/InfillAndFilamentTests.cs` | 6 | `TopoOpt` | `using TopoOpt.Core.Tests.Fixtures;` |
| `tests/TopoOpt.Core.Tests/Workflow/InfillAndFilamentTests.cs` | 7 | `TopoOpt` | `using TopoOpt.Core.Workflow;` |
| `tests/TopoOpt.Core.Tests/Workflow/InfillAndFilamentTests.cs` | 10 | `TopoOpt` | `namespace TopoOpt.Core.Tests.Workflow;` |
| `tests/TopoOpt.Core.Tests/Workflow/WorkflowTests.cs` | 7 | `TopoOpt` | `using TopoOpt.Core.Fem;` |
| `tests/TopoOpt.Core.Tests/Workflow/WorkflowTests.cs` | 8 | `TopoOpt` | `using TopoOpt.Core.Geometry;` |
| `tests/TopoOpt.Core.Tests/Workflow/WorkflowTests.cs` | 9 | `TopoOpt` | `using TopoOpt.Core.Tests.Fixtures;` |
| `tests/TopoOpt.Core.Tests/Workflow/WorkflowTests.cs` | 10 | `TopoOpt` | `using TopoOpt.Core.Voxelization;` |
| `tests/TopoOpt.Core.Tests/Workflow/WorkflowTests.cs` | 11 | `TopoOpt` | `using TopoOpt.Core.Workflow;` |
| `tests/TopoOpt.Core.Tests/Workflow/WorkflowTests.cs` | 14 | `TopoOpt` | `namespace TopoOpt.Core.Tests.Workflow;` |
| `tools/generate_rename_plan.py` | 47 | `TopoOpt` | `elif 'namespace TopoOpt' in line or 'using TopoOpt' in line:` |
| `tools/generate_rename_plan.py` | 47 | `TopoOpt` | `elif 'namespace TopoOpt' in line or 'using TopoOpt' in line:` |

### 3. Assembly and Directory.Build.props
- **Total Matches**: 5 across 2 file(s)
| File Path | Line | Occurrence | Context |
|-----------|------|------------|---------|
| `Directory.Build.props` | 7 | `TopoOpt` | `<Authors>TopoOpt Contributors</Authors>` |
| `Directory.Build.props` | 8 | `TopoOpt` | `<Company>TopoOpt</Company>` |
| `Directory.Build.props` | 9 | `TopoOpt` | `<Product>TopoOpt — 3D Topology Optimizer</Product>` |
| `Directory.Build.props` | 10 | `TopoOpt` | `<Copyright>Copyright (C) 2026 TopoOpt Contributors. Licensed under GNU GPLv3.</Copyright>` |
| `src/TopoOpt.Core/TopoOpt.Core.csproj` | 13 | `TopoOpt` | `<InternalsVisibleTo Include="TopoOpt.Core.Tests" />` |

### 4. XAML Classes, Namespaces & avares:// URIs
- **Total Matches**: 21 across 3 file(s)
| File Path | Line | Occurrence | Context |
|-----------|------|------------|---------|
| `src/TopoOpt.App/App.axaml` | 3 | `TopoOpt` | `x:Class="TopoOpt.App.App"` |
| `src/TopoOpt.App/App.axaml` | 9 | `TopoOpt` | `<ResourceInclude Source="avares://TopoOpt.App/Styles/Theme.axaml" />` |
| `src/TopoOpt.App/App.axaml` | 10 | `TopoOpt` | `<ResourceInclude Source="avares://TopoOpt.App/Styles/Icons.axaml" />` |
| `src/TopoOpt.App/Styles/Theme.axaml` | 63 | `TopoOpt` | `<x:String x:Key="App.Title">TopoOpt — Topology Optimization</x:String>` |
| `src/TopoOpt.App/Styles/Theme.axaml` | 73 | `TopoOpt` | `<x:String x:Key="Toolbar.About.Tooltip">About TopoOpt, licenses, and disclaimers</x:String>` |
| `src/TopoOpt.App/Styles/Theme.axaml` | 203 | `TopoOpt` | `<x:String x:Key="StatusBar.Ready">TopoOpt v1.0 — Ready</x:String>` |
| `src/TopoOpt.App/Views/MainWindow.axaml` | 3 | `TopoOpt` | `xmlns:vm="using:TopoOpt.App.ViewModels"` |
| `src/TopoOpt.App/Views/MainWindow.axaml` | 4 | `TopoOpt` | `xmlns:controls="using:TopoOpt.App.Controls"` |
| `src/TopoOpt.App/Views/MainWindow.axaml` | 5 | `TopoOpt` | `xmlns:rendering="using:TopoOpt.App.Rendering"` |
| `src/TopoOpt.App/Views/MainWindow.axaml` | 6 | `TopoOpt` | `xmlns:vox="using:TopoOpt.Core.Voxelization"` |
| `src/TopoOpt.App/Views/MainWindow.axaml` | 7 | `TopoOpt` | `xmlns:wf="using:TopoOpt.Core.Workflow"` |
| `src/TopoOpt.App/Views/MainWindow.axaml` | 8 | `TopoOpt` | `x:Class="TopoOpt.App.Views.MainWindow"` |
| `src/TopoOpt.App/Views/MainWindow.axaml` | 1076 | `TopoOpt` | `<TextBlock Text="TopoOpt v1.0.0" FontSize="18" FontWeight="Bold" Foreground="{DynamicResource TextPrimary}" />` |
| `src/TopoOpt.App/Views/MainWindow.axaml` | 1086 | `TopoOpt` | `<TextBlock Text="Structural stress distributions and topology-optimized geometries generated by TopoOpt are numerical engineering approximations based on linear elasticity and SIMP homogenization. FDM/FFF 3D prints exhibit significant interlayer anisotropy, porosity, and thermal variances. Physical load testing and professional FEA verification are mandatory before deploying designs in structural, safety-critical, or load-bearing applications."` |
| `src/TopoOpt.App/Views/MainWindow.axaml` | 1121 | `TopoOpt` | `<TextBlock Text="TopoOpt — Comprehensive User &amp; Technical Guide" FontSize="18" FontWeight="Bold" Foreground="{DynamicResource TextPrimary}" />` |
| `src/TopoOpt.App/Views/MainWindow.axaml` | 1137 | `TOPOOPT` | `<TextBlock Text="WHAT IS TOPOOPT?" FontSize="12" FontWeight="Bold" Foreground="{DynamicResource Accent}" />` |
| `src/TopoOpt.App/Views/MainWindow.axaml` | ... | ... | *(and 5 more occurrences)* |

### 5. Embedded Resources & Strings.resx
- **Total Matches**: 4 across 1 file(s)
| File Path | Line | Occurrence | Context |
|-----------|------|------------|---------|
| `src/TopoOpt.App/Resources/Strings.resx` | 16 | `TopoOpt` | `<value>TopoOpt — 3D Topology Optimizer</value>` |
| `src/TopoOpt.App/Resources/Strings.resx` | 48 | `TopoOpt` | `<value>TopoOpt — Topology Optimization</value>` |
| `src/TopoOpt.App/Resources/Strings.resx` | 78 | `TopoOpt` | `<value>About TopoOpt, licenses, and disclaimers</value>` |
| `src/TopoOpt.App/Resources/Strings.resx` | 444 | `TopoOpt` | `<value>TopoOpt v1.0 — Ready</value>` |

### 6. Window Title, About Dialog & Status Bar
- **Total Matches**: 2 across 1 file(s)
| File Path | Line | Occurrence | Context |
|-----------|------|------------|---------|
| `tools/generate_rename_plan.py` | 49 | `TopoOpt` | `elif 'Title' in line or 'About' in line or 'TopoOpt v' in line or 'TopoOpt — ' in line:` |
| `tools/generate_rename_plan.py` | 49 | `TopoOpt` | `elif 'Title' in line or 'About' in line or 'TopoOpt v' in line or 'TopoOpt — ' in line:` |

### 7. CLI Command Name & Help Text
- **Total Matches**: 8 across 1 file(s)
| File Path | Line | Occurrence | Context |
|-----------|------|------------|---------|
| `src/TopoOpt.Cli/Program.cs` | 9 | `TopoOpt` | `using TopoOpt.Core.Fem;` |
| `src/TopoOpt.Cli/Program.cs` | 10 | `TopoOpt` | `using TopoOpt.Core.Geometry;` |
| `src/TopoOpt.Cli/Program.cs` | 11 | `TopoOpt` | `using TopoOpt.Core.Optimization;` |
| `src/TopoOpt.Cli/Program.cs` | 12 | `TopoOpt` | `using TopoOpt.Core.Voxelization;` |
| `src/TopoOpt.Cli/Program.cs` | 14 | `TopoOpt` | `namespace TopoOpt.Cli;` |
| `src/TopoOpt.Cli/Program.cs` | 57 | `TopoOpt` | `Console.WriteLine("TopoOpt CLI — Usage:");` |
| `src/TopoOpt.Cli/Program.cs` | 58 | `topoopt` | `Console.WriteLine("  topoopt analyze <model.json>");` |
| `src/TopoOpt.Cli/Program.cs` | 59 | `topoopt` | `Console.WriteLine("  topoopt optimize <model.json> [--out <dir>]");` |

### 8. AppData & Log Folders
- **Total Matches**: 18 across 5 file(s)
| File Path | Line | Occurrence | Context |
|-----------|------|------------|---------|
| `docs/STEP8_AUDIT.md` | 10 | `TopoOpt` | `- Records any interval $> 100\text{ ms}$ with timestamp, active operation name, and duration to `~/.local/share/TopoOpt/logs/ui_gaps.log` and console.` |
| `src/TopoOpt.App/Diagnostics/CrashLogger.cs` | 8 | `TopoOpt` | `namespace TopoOpt.App.Diagnostics;` |
| `src/TopoOpt.App/Diagnostics/CrashLogger.cs` | 12 | `TopoOpt` | `/// into %LOCALAPPDATA%/TopoOpt/logs (Windows) or ~/.local/share/TopoOpt/logs (Linux),` |
| `src/TopoOpt.App/Diagnostics/CrashLogger.cs` | 12 | `TopoOpt` | `/// into %LOCALAPPDATA%/TopoOpt/logs (Windows) or ~/.local/share/TopoOpt/logs (Linux),` |
| `src/TopoOpt.App/Diagnostics/CrashLogger.cs` | 21 | `TopoOpt` | `"TopoOpt",` |
| `src/TopoOpt.App/Diagnostics/CrashLogger.cs` | 61 | `TopoOpt` | `writer.WriteLine("TopoOpt Crash Diagnostics Report");` |
| `src/TopoOpt.App/Diagnostics/CrashLogger.cs` | 98 | `TopoOpt` | `Console.Error.WriteLine($"[TopoOpt Crash] Logged to: {logPath}");` |
| `src/TopoOpt.App/Diagnostics/CrashLogger.cs` | 99 | `TopoOpt` | `Trace.WriteLine($"[TopoOpt Crash] Logged to: {logPath}");` |
| `src/TopoOpt.App/Diagnostics/CrashLogger.cs` | 103 | `TopoOpt` | `Console.Error.WriteLine($"[TopoOpt Crash] Failed to write crash log: {ioEx.Message}");` |
| `src/TopoOpt.App/Diagnostics/OrbitDiagnostics.cs` | 9 | `TopoOpt` | `namespace TopoOpt.App.Diagnostics;` |
| `src/TopoOpt.App/Diagnostics/OrbitDiagnostics.cs` | 13 | `TOPOOPT` | `/// Activated via --debug-orbit flag or TOPOOPT_DEBUG_ORBIT=1 environment variable.` |
| `src/TopoOpt.App/Diagnostics/OrbitDiagnostics.cs` | 14 | `TopoOpt` | `/// Logs to console and to %LOCALAPPDATA%/TopoOpt/logs/orbit_diag.log (or Linux equivalent).` |
| `src/TopoOpt.App/Diagnostics/OrbitDiagnostics.cs` | 43 | `TOPOOPT` | `string? env = Environment.GetEnvironmentVariable("TOPOOPT_DEBUG_ORBIT");` |
| `src/TopoOpt.App/Diagnostics/StressMeshGenerator.cs` | 5 | `TopoOpt` | `using TopoOpt.Core.Geometry;` |
| `src/TopoOpt.App/Diagnostics/StressMeshGenerator.cs` | 7 | `TopoOpt` | `namespace TopoOpt.App.Diagnostics;` |
| `src/TopoOpt.App/Diagnostics/StressMeshGenerator.cs` | 15 | `TopoOpt` | `public static string TempDir { get; } = Path.Combine(Path.GetTempPath(), "TopoOpt_Stress");` |
| `tools/generate_rename_plan.py` | 37 | `TopoOpt` | `elif 'CrashLogger' in rel_path or 'OrbitDiagnostics.cs' in rel_path or 'StressMeshGenerator.cs' in rel_path or 'TopoOpt_Stress' in line or 'TOPOOPT_DEBUG_ORBIT' in line or 'AppData' in line or 'ui_gaps.log' in line:` |
| `tools/generate_rename_plan.py` | 37 | `TOPOOPT` | `elif 'CrashLogger' in rel_path or 'OrbitDiagnostics.cs' in rel_path or 'StressMeshGenerator.cs' in rel_path or 'TopoOpt_Stress' in line or 'TOPOOPT_DEBUG_ORBIT' in line or 'AppData' in line or 'ui_gaps.log' in line:` |

### 9. STL Export Header Text
- **Total Matches**: 12 across 7 file(s)
| File Path | Line | Occurrence | Context |
|-----------|------|------------|---------|
| `docs/STEP6.md` | 103 | `TopoOpt` | `- **Export STL (`WorkflowService.ExportIterationStlAsync`)**: Extracts the iso-surface at the chosen threshold, applies $0..10$ iterations of volume-preserving Taubin smoothing ($\lambda = 0.50, \mu = -0.53$), writes a binary STL with the 80-byte header `"TopoOpt it=<n> th=<t>"`, and immediately re-reads the file with `StlReader.Read` to verify watertightness and volume.` |
| `docs/STEP6.md` | 114 | `TopoOpt` | `- **`T6_VerifyAndExport_EndToEnd`**: Verified thresholded FEA compliance within 15% of penalised SIMP compliance and exported binary STL re-reads via `StlReader.Read` as watertight with custom `"TopoOpt it=5 th=0.50"` header.` |
| `docs/USER_GUIDE.md` | 61 | `TopoOpt` | `3. Click **Export STL**. TopoOpt extracts a closed, watertight 2-manifold triangle mesh via Constrained Surface Nets (with 1-voxel zero-padding and 3D digital-topology well-composedness repair), writes a binary STL with header `"TopoOpt it=<n> th=<t>"`, and automatically re-reads the file with `StlReader.Read` to verify watertightness and volume.` |
| `docs/USER_GUIDE.md` | 61 | `TopoOpt` | `3. Click **Export STL**. TopoOpt extracts a closed, watertight 2-manifold triangle mesh via Constrained Surface Nets (with 1-voxel zero-padding and 3D digital-topology well-composedness repair), writes a binary STL with header `"TopoOpt it=<n> th=<t>"`, and automatically re-reads the file with `StlReader.Read` to verify watertightness and volume.` |
| `src/TopoOpt.Core/Geometry/StlWriter.cs` | 6 | `TopoOpt` | `namespace TopoOpt.Core.Geometry;` |
| `src/TopoOpt.Core/Geometry/StlWriter.cs` | 28 | `TopoOpt` | `"TopoOpt Binary STL (mm)"u8.CopyTo(header);` |
| `src/TopoOpt.Core/Workflow/WorkflowService.cs` | 703 | `TopoOpt` | `$"TopoOpt it={iteration} th={threshold:0.##}");` |
| `tests/TopoOpt.Core.Tests/Fixtures/StlFixtures.cs` | 365 | `TopoOpt` | `: "TopoOpt Binary STL"u8;` |
| `tests/TopoOpt.Core.Tests/Step6/Step6Tests.cs` | 153 | `TopoOpt` | `// Check 80-byte binary header starts with "TopoOpt it=42 th=0.5"` |
| `tests/TopoOpt.Core.Tests/Step6/Step6Tests.cs` | 160 | `TopoOpt` | `Assert.StartsWith("TopoOpt it=42 th=0.5", headerText);` |
| `tools/generate_rename_plan.py` | 39 | `TopoOpt` | `elif 'StlWriter' in rel_path or 'TopoOpt it=' in line or 'TopoOpt Binary STL' in line:` |
| `tools/generate_rename_plan.py` | 39 | `TopoOpt` | `elif 'StlWriter' in rel_path or 'TopoOpt it=' in line or 'TopoOpt Binary STL' in line:` |

---

## 3. Preservation Rules (Items that Must NOT be Renamed)

1. **Binary Magic Bytes (`"TOPO"u8`)**:
   - `src/TopoOpt.Core/Persistence/RunWriter.cs` (line 223) writes `"TOPO"u8` as the 4-byte magic signature for `history.bin` and `.topo` project archives.
   - `src/TopoOpt.Core/Persistence/RunReader.cs` (line 193) verifies `hdr[0..4].SequenceEqual("TOPO"u8)`.
   - **Reason**: Existing `.topo` projects and optimization run histories must open without data corruption or breaking changes. This magic signature is preserved byte-for-byte.
2. **JSON Schema Keys & Format Versions**:
   - `FormatVersion = 1` and `Version = 1` in `manifest.json`, `project.json`, and `domain.bin.br` are preserved byte-for-byte.
   - Readers do not validate the stored application name string, ensuring legacy files open seamlessly.
3. **Domain & Mathematical Terminology**:
   - The word "topology" and class names containing topology (e.g. `TopologyOptimizer`, `TopologyOptimizationResult`, `TopologyOptimizationOptions`) are domain terms, do not contain the brand name "TopoOpt", and remain unchanged.
4. **Git Commit History**:
   - File history is preserved via `git mv`.

---

## 4. Architectural & Identifier Decisions

| Component | Decision | Rationale |
|-----------|----------|-----------|
| **Namespaces** | `Mycel.Core`<br>`Mycel.App`<br>`Mycel.Cli`<br>`Mycel.Core.Tests` | Clean, idiomatic .NET namespace hierarchy matching directory layout. |
| **Solution File** | `Mycel.sln` | Replaces `TopoOpt.sln`, tracking the four renamed projects. |
| **Directory Names** | `src/Mycel.Core`<br>`src/Mycel.App`<br>`src/Mycel.Cli`<br>`tests/Mycel.Core.Tests` | Matches namespaces 1:1. |
| **GUI AssemblyName** | `Mycel` | Output executable: `Mycel.exe` (Windows) and `Mycel` (Linux). Identical casing on both OS. |
| **CLI AssemblyName** | `mycel-cli` | Output executable: `mycel-cli.exe` (Windows) and `mycel-cli` (Linux). Eliminates binary collision with GUI apphost. |
| **CLI Command Name** | `mycel-cli analyze <model.json>`<br>`mycel-cli optimize <model.json> [--out <dir>]` | Consistent command verb syntax and help documentation. |
| **App-Data & Log Path** | `%LOCALAPPDATA%\Mycel` (Windows)<br>`~/.local/share/Mycel` (Linux) | Follows OS standards. Automatic one-time migration copies settings from old `TopoOpt` folder if present, preserving old folder intact. |
| **Window Title** | `Mycel — Topology Optimization` | Configured via `App.Title` resource key. |
| **Status Bar Ready** | `Mycel v1.0 — Ready` | Configured via `StatusBar.Ready` resource key. |
| **STL Export Header** | `"Mycel it={iter} th={thresh}"` | 80-byte binary header text identifies STL origin. |
| **Archive Artifacts** | `Mycel-<version>-linux-x64.tar.gz`<br>`Mycel-<version>-win-x64.zip` | Updated in `build.sh`, `build.ps1`, and `.github/workflows/ci.yml`. |
| **Application Icons** | `resources/logo.jpeg` → multi-res ICO + PNGs | Rounded-square alpha mask (20% corner radius), Lanczos downsampled. |
