# Copyless

Copy-on-select for Windows. Selected text is copied to the clipboard as soon as you release the mouse button.

Copyless brings the X11 primary selection workflow to Windows: select text in any application and it is
ready to paste with Ctrl+V, without pressing Ctrl+C. Text that cannot be selected, such as text in games,
images and video, can be copied with the built-in Windows OCR engine.

## Contents

- [Features](#features)
- [Requirements](#requirements)
- [Installation](#installation)
- [Usage](#usage)
- [Configuration](#configuration)
- [Command-line options](#command-line-options)
- [How it works](#how-it-works)
- [Known limitations](#known-limitations)
- [Building from source](#building-from-source)
- [Uninstalling](#uninstalling)
- [Privacy](#privacy)

## Features

- Copies text selected with the mouse in any application: drag, double-click, triple-click and Shift+click.
- Copies keyboard selections (Shift+arrow keys, Shift+Home/End, Ctrl+A) when the modifier key is released.
- Copies text from any area of the screen using Windows OCR, including games, images, video and remote sessions.
- Smart paste: pressing Ctrl+V right after a selection replaces the selection with the previous clipboard contents.
- Optional middle-click paste.
- Works in terminals and in applications running as administrator.
- Per-application exclusions. Graphics editors are excluded by default so that dragging still draws.
- Keeps rich formatting when copying from browsers and office applications.
- Updates itself from the project's releases. Automatic updates can be turned off.
- Runs silently in the notification area. No telemetry and no runtime dependencies beyond Windows.

## Requirements

- Windows 10 version 1809 or later, or Windows 11
- .NET Framework 4.8 (included with Windows 10 version 1903 and later)
- Administrator rights to install
- For OCR: at least one Windows display language with OCR support (installed with most languages)

## Installation

1. Download [`Copyless-Setup.exe`](https://github.com/kokixzz/copyless/releases/latest/download/Copyless-Setup.exe)
   from the [latest release](https://github.com/kokixzz/copyless/releases/latest).
2. Run it and select **Yes** when asked to install.
3. Approve the User Account Control prompt.

Copyless starts immediately and on every logon. The installer is not code-signed, so Windows SmartScreen
may show a warning on first run; select **More info**, then **Run anyway**.

Copyless checks for new releases every 6 hours and installs them automatically. To update manually, choose
**Check for updates** in the tray menu, or run a newer `Copyless-Setup.exe` over the existing installation.
Settings are kept in both cases.

Because the installer is unsigned, it is worth checking that the download is the file that was published.
Compare the output with the SHA-256 in the release notes:

```powershell
Get-FileHash .\Copyless-Setup.exe
```

Prefer to compile it yourself? See [Building from source](#building-from-source).

| Item            | Location                                                         |
| --------------- | ---------------------------------------------------------------- |
| Program         | `C:\Program Files\Copyless\Copyless.exe`                         |
| Settings        | `%APPDATA%\Copyless\settings.ini`                                |
| Startup         | Task Scheduler task `Copyless` (at logon of the installing user) |
| Shortcut        | Start menu                                                       |
| Uninstall entry | Settings > Apps                                                  |

The startup task runs with highest privileges so that Copyless can read selections in elevated windows and
in games started as administrator. Windows does not allow a standard process to do this.

## Usage

| Action                                     | Result                                                 |
| ------------------------------------------ | ------------------------------------------------------ |
| Drag over text                             | The selection is copied                                |
| Double-click or triple-click               | The word or line is copied                             |
| Shift+click                                | The extended selection is copied                       |
| Shift+arrow keys, Shift+Home/End           | The selection is copied when Shift is released         |
| Ctrl+A                                     | Everything is copied when Ctrl is released             |
| Hold Ctrl+Alt and drag a rectangle         | Text inside the rectangle is recognized and copied     |
| Ctrl+Alt+O                                 | Freezes the screen so a rectangle can be drawn for OCR |
| Select text, then press Ctrl+V immediately | The selection is replaced with the previous clipboard  |
| Ctrl+Alt+C, or click the tray icon         | Pauses or resumes Copyless                             |

Ctrl+C and Ctrl+V keep working as usual. The mouse click used for Ctrl+Alt+drag is not passed to the
application underneath, so it does not trigger actions in games.

Right-click the tray icon for options, OCR language selection and per-application exclusions.

## Configuration

Most options are available from the tray menu. All settings are stored in `%APPDATA%\Copyless\settings.ini`
and are reloaded when the tray menu is opened.

| Key                     | Default      | Description                                                         |
| ----------------------- | ------------ | ------------------------------------------------------------------- |
| `Enabled`               | `True`       | Copy selections automatically                                       |
| `KeyboardSelection`     | `True`       | Copy Shift+arrow key selections                                     |
| `CopyOnCtrlA`           | `True`       | Copy after Ctrl+A                                                   |
| `SmartPaste`            | `True`       | Ctrl+V right after a selection pastes the previous clipboard        |
| `MiddleClickPaste`      | `False`      | Middle-click over a text field pastes                               |
| `AccessibilityFallback` | `True`       | Read selections through UI Automation when Ctrl+C is not used       |
| `ToggleHotkey`          | `Ctrl+Alt+C` | Pause and resume                                                    |
| `OcrHotkey`             | `Ctrl+Alt+O` | Frozen-screen OCR                                                   |
| `OcrDragModifiers`      | `Ctrl+Alt`   | Modifiers for live OCR drag, or `None` to disable                   |
| `OcrLanguage`           | `auto`       | `auto` or a language tag such as `en-US`                            |
| `MinDragPixels`         | `6`          | Minimum mouse movement that counts as a selection                   |
| `Exclude`               | see file     | Process names (without `.exe`) where nothing is copied              |
| `ForceCtrlC`            | see file     | Process names that use a custom I-beam cursor                       |
| `AutoUpdate`            | `True`       | Check for new releases every 6 hours and install them               |
| `DebugLog`              | `False`      | Write diagnostic messages to `%APPDATA%\Copyless\log.txt`           |

## Command-line options

```
Copyless.exe --install [--quiet]      Install or update, and start at logon for the current user
Copyless.exe --uninstall [--quiet]    Remove the program, startup task and shortcuts
Copyless.exe --quit                   Close the running instance
Copyless.exe --ocr <image> <output>   Recognize text in an image file and write it to a file
```

## How it works

Copyless installs low-level mouse and keyboard hooks and detects selection gestures. When a gesture ends
over an I-beam cursor, Copyless sends Ctrl+C to the foreground window and confirms the copy through the
clipboard sequence number, which preserves the formatting the application puts on the clipboard. When no
I-beam cursor is shown, when the window is a terminal, or when Ctrl+C has no effect, the selection is read
through Microsoft UI Automation instead. Terminals never receive Ctrl+C, so running programs are not
interrupted.

OCR captures the selected screen area, scales it up for small text, and passes it to `Windows.Media.Ocr`.
When more than one recognition language is installed, the result whose script best matches the text is used.

Updates are checked 45 seconds after start and every 6 hours through the GitHub releases API. A newer
installer is downloaded, verified against the size and SHA-256 published with the release and against its
file version, and then run silently. It closes the running instance, replaces the program and starts it again.

## Known limitations

- Games running in exclusive fullscreen mode cannot be captured for OCR. Use borderless or windowed mode.
- Password fields are never copied. Windows blocks copying from them.
- Every selection replaces the clipboard. Windows clipboard history (Win+V) can be used to recover earlier
  items.
- Chromium-based browsers enable their accessibility tree when queried through UI Automation, which slightly
  increases their memory use.
- OCR accuracy depends on the installed Windows language packs.

## Building from source

The project builds with the C# compiler included in the .NET Framework, so no SDK or Visual Studio
installation is needed.

Run the build script from the project folder:

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

The script produces `dist\Copyless.exe`, which is the same program the release ships as
`Copyless-Setup.exe`. Run it and select **Yes** when asked to install, as in
[Installation](#installation).

| File                  | Purpose                                                 |
| --------------------- | ------------------------------------------------------- |
| `src\InputWatcher.cs` | Mouse and keyboard hooks, gesture detection             |
| `src\CopyEngine.cs`   | Copy strategy, smart paste                              |
| `src\Uia.cs`          | Selection reading through UI Automation                 |
| `src\Ocr.cs`          | Screen capture and text recognition                     |
| `src\TrayApp.cs`      | Tray icon, menu and hotkeys                             |
| `src\Ui.cs`           | Icon drawing, overlays and the region picker            |
| `src\Program.cs`      | Entry point, installer and startup task                 |
| `src\Settings.cs`     | Settings file                                           |
| `src\Native.cs`       | Win32 interop                                           |

## Uninstalling

Open **Settings > Apps**, select **Copyless** and choose **Uninstall**, or run:

```powershell
& "C:\Program Files\Copyless\Copyless.exe" --uninstall
```

Settings in `%APPDATA%\Copyless` are kept and can be deleted manually.

## Privacy

Copyless does not record keystrokes or clipboard contents. Selected text is only placed on the Windows
clipboard. The only network access is the update check, which requests the latest release information from
GitHub. The request identifies only the Copyless version. It can be turned off with **Update automatically** in
the tray menu. OCR runs locally through Windows. The optional debug log records application names and
gesture types, never text.
