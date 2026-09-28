# examples/ — padkit レシピ例

架空の CLI `mytool.exe`（stdout に JSON を返す）を PAD からキックする例。
ドメイン語彙を含まないので、そのまま自分の CLI/パスに置き換えて使える。

| ファイル | 内容 | 使っているブロック |
|---|---|---|
| 01-run-cli-to-csv | CLI 実行 → JSON パース → CSV 書き出し | run-cli-or-exit, write-text, info-dialog |
| 02-excel-rows | Excel を1行ずつ処理してセルへ書き戻し | excel-open, run-cli, append-log, json-parse, excel-close-save |
| 03-csv-to-json-args | CSV → JSON 文字列組み立て → CLI 引数 | read-csv, run-cli-or-exit, info-dialog（全角 `％` の例あり） |
| 04-csv-folder-batch | フォルダ内 CSV の一括処理・移動 | get-files, read-csv, run-cli, append-log, write-text, info-dialog |
| 05-retry-and-log | リトライ + ログ + エラー終了 | retry-cli, fail-dialog-exit, append-log, json-parse, info-dialog |
| 06-teams-notify | 実行結果を Teams Webhook 通知 | run-cli-or-exit, teams-notify, warn-dialog, info-dialog |
| 07-teams-status-card | 成否で色分け Adaptive Card | run-cli, json-parse, append-log, teams-notify, warn-dialog, info-dialog |

生成と検査:

```bash
# repo ルートから (padkit.exe は dotnet build -c Release で生成)
padkit render-all examples --profile examples/profiles/sample.json --out-dir out
padkit lint out/*.txt
```

`--profile` は複数回指定できる（後勝ち）。実 WebhookUrl 等の秘密値は
gitignore した `*.local.json` に置いてオーバーレイする。
