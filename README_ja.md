# Moodle Importer
[英語版](./README.md)

Moodleカレンダーから今後の課題を取得し、[todo.exe](https://github.com/Zenact5/todo-cli/releases) を介してMicrosoft Todoに登録します。

## 必要要件

- [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- [todo.exe](https://github.com/Zenact5/todo-cli/releases)
- Chromium（自動インストールされません。下記のステップ3で一度だけインストールが必要です）

## クイックスタート

```sh 
# 1. moodle-importer.exeをフォルダに展開

# 2. 設定テンプレートを生成
# 任意のshellで以下のコマンドを実行
moodle-importer.exe --init

# 3. Chromiumをインストール（初回のみ、PowerShellが必要）
powershell -ExecutionPolicy Bypass -File .\playwright.ps1 install chromium

# 4. .envに認証情報を入力
#    MOODLE_USERNAME=your_username
#    MOODLE_PASSWORD=your_password

# 5. todo.exeをTodo\サブフォルダに配置（または.envでTODO_CLI_PATHを設定）

# 6. 実行
moodle-importer.exe
```

## 設定（.env）

| 変数 | デフォルト値 | 説明 |
|---|---|---|
| `MOODLE_URL` | `https://moodle41.lms.ehime-u.ac.jp/moodle` | MoodleインスタンスのURL |
| `MOODLE_USERNAME` | — | Moodleログインユーザー名 |
| `MOODLE_PASSWORD` | — | Moodleログインパスワード |
| `TODO_CLI_PATH` | `.\Todo\todo.exe` | todo.exeのパス |
| `TODO_LIST_NAME` | `Univ` | 対象のMicrosoft Todoリスト名 |
| `DUE_CUTOFF_HOUR` | `4` | この時刻(0-23)より前の締切は前日として登録（例: 9/27 0:00 → 9/26）。`0` で正規化を無効化 |
| `TITLE_WHITELIST` | (空) | カンマ区切り。空でない場合、いずれかを含む課題名のみ登録（大文字小文字を区別しない部分一致） |
| `TITLE_BLACKLIST` | `開始,opens` | カンマ区切り。いずれかを含む課題名をスキップ（大文字小文字を区別しない部分一致）。空にすると無効化 |

### 設定のバージョン管理

`.env` には `# config-version: N` というコメント行が含まれます。
将来の新バージョンで変数が追加された場合、`moodle-importer.exe --init` を実行すると：

- 不足している変数がデフォルト値で末尾に追記されます（既存の設定値は一切変更されません）
- 廃止された変数は注記付きでコメントアウトされます
- 変更前のバックアップが `.env.bak` に保存されます

古い `.env` のまま通常実行した場合は警告のみが表示され、ファイルは変更されません。

## フラグ

| フラグ | 説明 |
|---|---|
| `--detail` | 詳細ログを表示（ログイン情報、タスクごとの出力など） |
| `--init` | .envテンプレートの生成（既存の.envは最新configバージョンへ移行）とディレクトリ構成の準備 |

## タスクスケジューラ（Windows）

毎日自動実行するタスクを設定：

```
トリガー: 毎日午前8:00
アクション: プログラムの開始
プログラム: path\to\moodle-importer.exe
```

> 注: 仕様上、実行中はターミナルウィンドウが表示されます。タスクのプロパティで「ユーザーがログオンしているかどうかにかかわらず実行する」を選択すると、ウィンドウは表示されません。


## ソースからのビルド

```sh
git clone <repo-url>
cd moodle-importer
dotnet build
# Chromiumが %USERPROFILE%\AppData\Local\ms-playwright にインストールされる（Debug/Releaseで共有）
powershell -ExecutionPolicy Bypass -File bin\Debug\net8.0\playwright.ps1 install chromium
dotnet publish -c Release -r win-x64
```

出力は `bin/Release/net8.0/win-x64/publish/` に生成されます。

## リリース配布

リリースを配布する場合：

1. ビルド（上記参照）
2. `bin/Release/net8.0/win-x64/publish/` フォルダ全体をリリースのベースとしてコピー
3. `*.pdb` ファイルを削除（デバッグシンボル、不要）
4. リリース内の `Todo\` サブフォルダに `todo.exe` を配置
5. すべてをzipにまとめる

zipには `playwright.ps1` と `.playwright\` フォルダを含めてください（利用者のChromiumインストールに必要）。

利用者の手順：
1. zipを展開
2. `powershell -ExecutionPolicy Bypass -File .\playwright.ps1 install chromium` を実行（初回のみ）
3. `moodle-importer.exe --init` を実行して `.env` を生成
4. `.env` に認証情報を入力
5. `moodle-importer.exe` を実行（タスクスケジューラで自動化が可能）

## ライセンス

[MIT](./LICENSE)
