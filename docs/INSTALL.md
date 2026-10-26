# TopoOpt Installation & Runtime Requirements

This document describes the prerequisites, installation steps, and graphics troubleshooting for running **TopoOpt** on Linux and Windows platforms.

---

## 1. Linux Runtime Requirements

TopoOpt is distributed as a self-contained single-file executable (`dist/TopoOpt-<version>-linux-x64.tar.gz`), requiring no external .NET runtime installation. However, it requires standard Linux desktop graphics and font libraries:

### System Libraries
- **OpenGL / Mesa**:
  - Requires OpenGL 3.3+ Core profile or OpenGL ES 3.0+ drivers (`libGL.so.1`, `libEGL.so.1`).
  - Supported hardware: Intel HD Graphics (Gen7+), AMD Radeon (GCN1+ / RDNA), NVIDIA (GeForce 400+ series).
  - Software rendering fallback: Mesa llvmpipe / softpipe (`libgl1-mesa-dri`).
- **Font Rendering**:
  - `libfontconfig1` (`libfontconfig.so.1`)
  - Standard TrueType / OpenType font packages (e.g. `fonts-dejavu-core`, `liberation-fonts`).
- **Display Servers**:
  - **X11**: Supported out of the box.
  - **Wayland**: Supported via native Wayland or through XWayland. If running on native Wayland compositor (GNOME Shell, Sway, KDE Plasma 6), Avalonia automatically detects Wayland or falls back to XWayland transparently.

### Package Manager Installation Commands

#### Fedora / RHEL / Nobara:
```bash
sudo dnf install mesa-dri-drivers mesa-libGL fontconfig freetype libX11 libXcursor libXi libXrandr
```

#### Ubuntu / Debian / Pop!_OS:
```bash
sudo apt-get update
sudo apt-get install libgl1-mesa-glx libgl1-mesa-dri libfontconfig1 libfreetype6 libx11-6 libxcursor1 libxi6 libxrandr2
```

#### Arch Linux / Manjaro:
```bash
sudo pacman -S mesa fontconfig freetype2 libx11 libxcursor libxi libxrandr
```

---

## 2. Graphics Troubleshooting & Software Fallback

If running on a virtual machine, headless server, or legacy GPU without OpenGL 3.3 hardware support, TopoOpt detects OpenGL initialization errors and warns rather than crashing.

To force software rasterization via Mesa llvmpipe:
```bash
LIBGL_ALWAYS_SOFTWARE=1 ./TopoOpt
```

To run with explicit Wayland or X11 backend:
```bash
# Force X11 / XWayland backend:
AVALONIA_SCREEN_BACKEND=x11 ./TopoOpt

# Force Wayland native backend:
AVALONIA_SCREEN_BACKEND=wayland ./TopoOpt
```

---

## 3. Windows Requirements

- Windows 10 (version 1809+) or Windows 11 (x64).
- DirectX 11 / OpenGL 3.3 compatible graphics driver.
- TopoOpt for Windows (`dist/TopoOpt-<version>-win-x64.zip`) is fully self-contained; no .NET Framework or .NET Desktop Runtime installation is required.

---

## 4. Building from Source

Requirements:
- .NET 10.0 SDK (`dotnet --version` >= 10.0.100).
- Standard build tools (`git`, `bash`, `tar`, `zip`).

```bash
# Clone the repository
git clone https://github.com/simone/StlOptimizer.git
cd StlOptimizer

# Run automated tests and produce release packages in dist/
./build.sh
```

