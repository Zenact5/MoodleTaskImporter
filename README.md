# Moodle Importer

[日本語版](./README_ja.md)

Scrapes upcoming assignments from Moodle calendar and registers them in Microsoft Todo via [todo.exe](https://github.com/Zenact5/todo-cli/releases).

## Requirements

- [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- [todo.exe](https://github.com/Zenact5/todo-cli/releases)
- Chromium (auto-installed by Playwright on first run)

## Quick Start

```sh
# 1. Extract moodle-importer.exe to a folder

# 2. Generate config template
moodle-importer.exe --init

# 3. Edit .env with your credentials
#    MOODLE_USERNAME=your_username
#    MOODLE_PASSWORD=your_password

# 4. Place todo.exe in the Todo\ subfolder (or set TODO_CLI_PATH in .env)

# 5. Run
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

## Flags

| Flag | Description |
|---|---|
| `--detail` | Show verbose logs (login details, per-task output, etc.) |
| `--init` | Generate .env template and prepare directory structure |

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
dotnet restore
playwright install chromium
dotnet publish -c Release -r win-x64 --self-contained true
```

The output will be in `bin/Release/net8.0/win-x64/publish/`.

## Release Distribution

To distribute the release:

1. Build (see above)
2. Copy the entire `bin/Release/net8.0/win-x64/publish/` folder as your release base
3. Delete `*.pdb` files (debug symbols, not needed)
4. Place `todo.exe` into a `Todo\` subfolder inside the release
5. Zip everything together

Users then:
1. Extract the zip
2. Run `moodle-importer.exe --init` to generate `.env`
3. Edit `.env` with their credentials
4. Run `moodle-importer.exe` (via Task Scheduler for automation)

## License

[MIT](./LICENSE)
