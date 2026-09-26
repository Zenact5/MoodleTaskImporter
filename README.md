# Moodle Importer

[日本語版](./README_ja.md)

Scrapes upcoming assignments from Moodle calendar and registers them in Microsoft Todo via [todo.exe](https://github.com/Zenact5/todo-cli/releases).

## Requirements

- [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- [todo.exe](https://github.com/Zenact5/todo-cli/releases)
- Chromium (NOT auto-installed; install once via `playwright.ps1`, see step 3 below)

## Quick Start

```sh
# 1. Extract moodle-importer.exe to a folder

# 2. Generate config template
moodle-importer.exe --init

# 3. Install Chromium (first run only, requires PowerShell)
powershell -ExecutionPolicy Bypass -File .\playwright.ps1 install chromium

# 4. Edit .env with your credentials
#    MOODLE_USERNAME=your_username
#    MOODLE_PASSWORD=your_password

# 5. Place todo.exe in the Todo\ subfolder (or set TODO_CLI_PATH in .env)

# 6. Run
moodle-importer.exe
```

## Configuration (.env)

| Variable | Default | Description |
|---|---|---|
| `MOODLE_URL` | `https://moodle41.lms.ehime-u.ac.jp/moodle` | Moodle instance URL |
| `MOODLE_USERNAME` | — | Moodle login username |
| `MOODLE_PASSWORD` | — | Moodle login password |
| `TODO_CLI_PATH` | `.\Todo\todo.exe` | Path to todo.exe |
| `TODO_LIST_NAME` | `Univ` | Target Microsoft Todo list name |
| `DUE_CUTOFF_HOUR` | `4` | Due times before this hour (0-23) are registered as the previous day (e.g. 9/27 0:00 → 9/26). `0` disables normalization |
| `TITLE_WHITELIST` | (empty) | Comma-separated; if not empty, only assignments whose title contains any entry are registered (case-insensitive) |
| `TITLE_BLACKLIST` | `開始,opens` | Comma-separated; assignments whose title contains any entry are skipped (case-insensitive). Set to an empty value to disable |

### Config versioning

`.env` carries a `# config-version: N` comment. When new variables are added in a
future release, run `moodle-importer.exe --init`:

- Missing variables are appended with their default values (your existing values are never touched)
- Deprecated variables are commented out with a note
- A backup of the previous file is written to `.env.bak`

On a normal run with an outdated `.env`, a warning is shown but nothing is modified.

## Flags

| Flag | Description |
|---|---|
| `--detail` | Show verbose logs (login details, per-task output, etc.) |
| `--init` | Generate .env template, or migrate an existing .env to the latest config version, and prepare directory structure |

## Task Scheduler (Windows)

Set up a daily task to run automatically:

```
Trigger: Daily at 8:00 AM
Action: Start a program
Program: path\to\moodle-importer.exe
```

## Build from Source

```sh
git clone <repo-url>
cd moodle-importer
dotnet build
# Installs Chromium into %USERPROFILE%\AppData\Local\ms-playwright (shared by Debug/Release)
powershell -ExecutionPolicy Bypass -File bin\Debug\net8.0\playwright.ps1 install chromium
dotnet publish -c Release -r win-x64
```

The output will be in `bin/Release/net8.0/win-x64/publish/`.

## Release Distribution

To distribute the release:

1. Build (see above)
2. Copy the entire `bin/Release/net8.0/win-x64/publish/` folder as your release base
3. Delete `*.pdb` files (debug symbols, not needed)
4. Place `todo.exe` into a `Todo\` subfolder inside the release
5. Zip everything together

Keep `playwright.ps1` and the `.playwright\` folder in the zip; users need them to install Chromium.

Users then:
1. Extract the zip
2. Run `powershell -ExecutionPolicy Bypass -File .\playwright.ps1 install chromium` (first run only)
3. Run `moodle-importer.exe --init` to generate `.env`
4. Edit `.env` with their credentials
5. Run `moodle-importer.exe` (via Task Scheduler for automation)

## License

[MIT](./LICENSE)
