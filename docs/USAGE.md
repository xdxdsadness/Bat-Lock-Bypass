# Usage

## GUI

1. Run `Bat Lock Bypass.exe` and confirm the UAC prompt (the packet driver
   needs administrator rights). The bypass starts automatically with preset 1.
2. The status line shows whether the bypass is active; the big button turns
   it on and off. The bypass keeps working in the background while the
   `Bat lock bypass` process is alive — closing the window does not stop it.
3. «Check all ways» runs every preset in turn, measures how many target sites
   reply and how fast, then shows a rating. Double-click a row or press
   «Enable best» to switch to that preset.
4. On exit the app asks whether to stop the bypass.

The log file lives in `%LOCALAPPDATA%\BatLockBypass\gui.log`.

## Console

`console.ps1` needs the rest of the app next to it (`bypass-core.ps1`,
`presets.json`, `bin/`, `lists/`):

```powershell
powershell -ExecutionPolicy Bypass -File console.ps1                # interactive menu
powershell -ExecutionPolicy Bypass -File console.ps1 -Preset 16     # run preset 16
powershell -ExecutionPolicy Bypass -File console.ps1 -Preset fake   # by part of the name
powershell -ExecutionPolicy Bypass -File console.ps1 -Status        # is the bypass running
powershell -ExecutionPolicy Bypass -File console.ps1 -Stop          # stop the engine
```

The full scan with the rating lives in the GUI («Check all ways» button).

## Presets

The active bypass way is chosen in the dropdown. Each entry is a command line
for the engine; the list lives in `presets.json`. See [PRESETS.md](PRESETS.md).
