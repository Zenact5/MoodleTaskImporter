# Moodle Importer
[英語版](./README.md)

Moodleカレンダーから今後の課題を取得し、[todo.exe](https://github.com/Zenact5/todo-cli/releases) を介してMicrosoft Todoに登録します。

## 必要要件

- [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- [todo.exe](https://github.com/Zenact5/todo-cli/releases)
- Chromium（初回実行時にPlaywrightが自動インストール）

## クイックスタート

```sh 
# 1. moodle-importer.exeをフォルダに展開

# 2. 設定テンプレートを生成
# 任意のshellで以下のコマンドを実行
moodle-importer.exe --init

# 3. .envに認証情報を入力
#    MOODLE_USERNAME=your_username
#    MOODLE_PASSWORD=your_password

# 4. todo.exeをTodo\サブフォルダに配置（または.envでTODO_CLI_PATHを設定）

# 5. 実行
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

## フラグ

| フラグ | 説明 |
|---|---|
| `--detail` | 詳細ログを表示（ログイン情報、タスクごとの出力など） |
| `--init` | .envテンプレートを生成し、ディレクトリ構成を準備 |

## タスクスケジューラ（Windows）

毎日自動実行するタスクを設定：

```
トリガー: 毎日午前8:00
アクション: プログラムの開始
プログラム: path\to\moodle-importer.exe
```


## ソースからのビルド

```sh
git clone <repo-url>
cd moodle-importer
dotnet restore
playwright install chromium
dotnet publish -c Release -r win-x64 --self-contained true
```

出力は `bin/Release/net8.0/win-x64/publish/` に生成されます。

## リリース配布

リリースを配布する場合：

1. ビルド（上記参照）
2. `bin/Release/net8.0/win-x64/publish/` フォルダ全体をリリースのベースとしてコピー
3. `*.pdb` ファイルを削除（デバッグシンボル、不要）
4. リリース内の `Todo\` サブフォルダに `todo.exe` を配置
5. すべてをzipにまとめる

利用者の手順：
1. zipを展開
2. `moodle-importer.exe --init` を実行して `.env` を生成
3. `.env` に認証情報を入力
4. `moodle-importer.exe` を実行（タスクスケジューラで自動化が可能）

## ライセンス

[MIT](./LICENSE)
