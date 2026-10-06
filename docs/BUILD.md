# Building and branding

## GUI (`Bat Lock Bypass.exe`)

The GUI is built from the C# source `src/BatLockBypass.cs` with the compiler
that ships with Windows (.NET Framework 4.x) — no Visual Studio required:

```bat
tools\build_gui.bat
```

The script compiles `src/BatLockBypass.cs`, embeds `src/app.ico` and the
administrator manifest (`src/app.manifest`), and places the result in the
repository root as `Bat Lock Bypass.exe`. Run it after every change to the
source, the icon or the manifest.

The application version is set by the `[assembly: ...]` attributes at the top
of `src/BatLockBypass.cs` (`AssemblyVersion`, `AssemblyFileVersion`,
`AssemblyInformationalVersion`). Bump them there before building a new release.

## Engine branding (`bin/Bat lock bypass.exe`)

`tools/brand_engine.ps1` copies `bin/engine.exe` to `bin/Bat lock bypass.exe`
and applies the icon plus the version metadata with
[rcedit](https://github.com/electron/rcedit) (MIT). Task Manager then shows
"Bat lock bypass" with the app icon instead of a bare `engine.exe`.

Run it after every `bin/engine.exe` or `src/app.ico` update. The scripts
prefer the branded copy and fall back to plain `engine.exe` when it is missing.

## Console scripts

`console.ps1` and `bypass-core.ps1` are plain PowerShell — no build step is
needed. Keep them in the repository root next to `presets.json`, `bin/` and
`lists/`.
