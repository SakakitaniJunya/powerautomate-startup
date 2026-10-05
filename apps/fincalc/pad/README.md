# pad/ — PAD フロー生成物

`recipes/*.pad` (padkit レシピ) から `padkit render-all` で生成した PAD フローテキスト。
`*.txt` は生成物なので直接編集しない。

- `profiles/client.json` — 配布先用サンプル (C:\work\fincalc)
- `profiles/dev.json` — 本機環境 (gitignore 対象、`dev.json.example` をコピーして作成)
- `profiles/dev.local.json` — ローカル秘密値オーバーレイ (gitignore 対象、未コミット。
  `dev.local.json.example` をコピーして実 WebhookUrl を入れる)

`--profile` は複数回指定でき、後のファイルが同じキーを上書きする
(ローカルの秘密値だけ `dev.local.json` で差し替える)。

再生成:

```bash
# padkit は同 repo ルートで一度ビルドして使う (dotnet build src/PadKit.Cli -c Release)
PADKIT=${PADKIT:-src/PadKit.Cli/bin/Release/net8.0-windows/padkit.exe}
$PADKIT render-all apps/fincalc/pad/recipes --profile apps/fincalc/pad/profiles/client.json --out-dir apps/fincalc/pad
$PADKIT render-all apps/fincalc/pad/recipes --profile apps/fincalc/pad/profiles/dev.json    --out-dir apps/fincalc/run
# 実 WebhookUrl 入りで生成する場合 (dev.local.json は gitignore 対象):
$PADKIT render-all apps/fincalc/pad/recipes --profile apps/fincalc/pad/profiles/dev.json --profile apps/fincalc/pad/profiles/dev.local.json --out-dir apps/fincalc/run
```

貼り付け前の検査: `$PADKIT lint apps/fincalc/pad/*.txt`
実機チェック: `$PADKIT designer check apps/fincalc/run/*.txt` (PAD デザイナー起動中に)

## テンプレート一覧

| # | 用途 |
|---|---|
| 01 | exe → 償却スケジュール CSV 出力 |
| 02 | Excel 台帳行ループ → exe → セル書き戻し (Excel COM) |
| 03 | 請求明細 CSV → 税率別消費税 (インボイス) |
| 04 | 報酬 CSV → 源泉税一括 → 結果 CSV |
| 05 | リトライ・エラーログの共通パターン |
| 06 | in/ フォルダ内 CSV 一括 → done/ 移動 |
| 07 | xlsx 直接一括 (Excel 本体不要) + 結果ブック表示 |
| 08 | 計算結果を FactSet リッチカードで Teams 投稿 |
| 09 | 成否で good/attention カード色分けアラート |
| 10 | バッチ完了サマリ (全成功/一部エラー/起動失敗の3色) |
| 11 | 閾値監視 (計算結果 > 閾値 → ALERT カード) |
| 12 | 日次ダイジェスト (複数計算を ColumnSet 横並び KPI) |
| 13 | 承認依頼ループ (依頼カード → Yes/No → 結果カード) |
| 14 | 実行監視 (開始/終了の2段カードでジョブをトレース) |
| 15 | ボタン付きカード (Action.OpenUrl でファイル/チャット) |
| 16 | サブフロー分割の雛形 (Main+RunCalc/NotifyOk/NotifyError を CALL 委譲) |
| 17 | 部署別プロファイル → 異常あれば部長へ Outlook メール (5サブフロー) |
| 18 | 遅延理由の回収: 担当者ごとに複数案件の入力カード投稿 |
| 19 | 日次チェック: 未提出者へリマインド + 全員完了なら OK カード |

## Adaptive Card 組み立ての注意 (実機検証済み)

- **JSON 内に `%PathVar%` (バックスラッシュを含む変数) を埋め込まない**。
  `C:\Users\...` がそのまま JSON に入ると `\U` 等が不正エスケープになり、
  webhook は 202 を返しても投稿をサイレント廃棄する。パス表示は
  `out/dep_result.xlsx` のように `/` 区切りのリテラルで書く。
- `stderr` も同じ理由でカードに直接入れない (改行・引用符で壊れる)。
  ログファイルに追記し、カードには終了コードだけ載せる (09 参照)。
- webhook URL 中の `%` は `%%` にエスケープする
  (`%` は PAD の変数記法と衝突)。プロファイル値も `%%` 入りで持つ。
- 応答は HTTP 202 → `200 <= WebStatus <= 299` を成功判定にする。
- `IF` 条件で `Display.DialogResult.Yes` 等の enum 参照は貼り付け拒否。
  ボタン結果は `ButtonPressed = 'Yes'` の文字列比較で判定する。
- `DateTimeFormat.Date` は非メンバー。`Date`/`DateAndTime`/`DateOnly` が有効。

## Outlook メール送信 (17 番, PAD 11.2609 実機検証済み)

- `Outlook.Launch.Launch Instance=> X` →
  `Outlook.SendEmailThroughOutlook.SendEmail` → `Outlook.Close.Close Instance: X`
- `SendTo` は宛先**リスト**型。`[Batch.managerEmail]` のように `%` を付けない
  リストリテラルで渡す（`SendTo: %var%` は貼り付け拒否）
- `Account:` は空にできない → 送信元アカウント名を `MailAccount` 変数で渡す
- `IsDraft: True` にすると送信せず下書き保存（検証時に安全）
- 宛先の部長メールは部署プロファイル JSON の `managerEmail` 由来
  （fincalc の JSON 出力に載る。実アドレスは gitignore 側のプロファイルへ）

## 遅延台帳 (18/19 番)

- 台帳列: 担当者 / メール / 案件 / 期限 / 遅延理由 / 状態
  （`excel init-delay` で雛形生成、部署別列名は `--columns` で対応可）
- 状態は `未提出` / `一時保存` / `提出済`。`--draft` 提出は一時保存として残り、
  `delay-status` の未提出カウントに含まれる（督促対象）
- 19 番は日次ジョブとしてタスクスケジューラからフロー起動する運用を想定

詳細は repo ルートの README (padkit) を参照。
