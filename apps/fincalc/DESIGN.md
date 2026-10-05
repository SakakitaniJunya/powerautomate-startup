# fincalc 設計書 — PAD 連携・金融計算ツールキット

> **TL;DR**: AI が使えない実行環境へ持ち込む金融計算 PAD 自動化の一式。
> 計算は C# exe、PAD フローはレシピ→テキスト生成、通知は既存 Teams の
> Workflows webhook。生成/検査の padkit は同 repo (repo ルート)。

> 対象版: apps/fincalc + padkit(repo ルート src/) 構成
> 対象環境: Windows 11 / PAD 11.2609.183.0 / .NET 8 / 新 Teams

## 1. 目的・背景

金融計算の日次/月次作業を Power Automate Desktop (PAD) で自動化するにあたり、
**実行環境で AI が使えない**制約がある。このため:

- 計算ロジックは AI に依存せず C# 製 exe (fincalc.exe) に閉じ込める
- PAD フローはテキスト形式で持ち込み、貼り付けで復元できるようにする
- 貼り付け〜検証〜実行自体も UI Automation で機械化し、属人性を排除する
- 実行結果の通知は既存の Teams チャネルを使う（新規 SaaS を入れない）

## 2. 全体アーキテクチャ

```
┌─ PAD フロー (orchestration) ─────────────────────────────┐
│  CSV/Excel 読込 → fincalc.exe をキック → JSON パース     │
│     → CSV/Excel 出力 / Teams Webhook POST / ダイアログ   │
└──────┬───────────────────────────────────────┬─────────┘
       │ RunDOSCommand (stdout=JSON)           │ InvokeWebService (Adaptive Card)
       ▼                                       ▼
  fincalc.exe                            Teams Workflows webhook
  (FinCalc / FinCalc.Excel /             → 既存チーム test/一般 等
   FinCalc.Cli, .NET 8)
       ▲
       │ recipes/*.pad + profiles/*.json ─render-all→ pad/*.txt → PAD に貼付
  padkit (同 repo ルート: src/PadKit.*)
   render / lint / designer check / run
```

設計上の分業:

| 層 | 担当 | 理由 |
|---|---|---|
| 計算 | fincalc.exe (C#) | 利率表・端数処理を単体テストで担保。PAD には載せない |
| 手順 | PAD フロー (.pad → .txt) | 実行環境の標準ツール。テキスト管理で再現可能 |
| 通知 | Teams Workflows webhook | 既存チャネル。API キー・クラウドフロー不要 |
| 生成/検査 | padkit (同 repo) | バージョン依存構文をルール化し生成物を機械検査 |
| 検証自動化 | padkit designer | UIA3 で PAD を外から操作 |

## 3. コンポーネント設計

### 3.1 FinCalc (計算ライブラリ, net8.0)

- `Depreciation/DepreciationCalculator` — 定額法・200%定率法(定率法→保証率切替・
  改定償却率・均等償却)、償却率表内蔵、月割り (`months` 引数)、簿価1円まで
- `Tax/*` — 消費税(10%/軽減8%, インボイス方式=税率別合計→端数処理)、
  源泉所得税(復興特別所得税込 10.21% / 100万円超分 20.42%)、法人税概算
- 計算例は国税庁公表値との突合を xunit で回帰 (`tests/FinCalc.Tests` 21 件)

### 3.2 FinCalc.Excel (ClosedXML 0.105.1, Excel 本体不要)

- `SheetReader` — シート走査。先頭10行以内から既知ヘッダ列を探索
  (タイトル行付きの複雑な帳票を吸収)。ヘッダ行は `--header-row` で手動指定可
- `HeaderMap` — 列名エイリアス (資産名/asset, 取得価額/cost, 耐用年数/life,
  償却方法/定額/定率/straight/declining, 供用月数, 端数処理)
- `DepBatch` — `excel dep-batch`: 全シート横断→行ごと償却スケジュール→
  summary(結果+error列)と schedule シートを出力。1行失敗で止めない
- `InvoiceBatch` / `SampleWorkbook` — 請求集計 / サンプル生成

### 3.3 FinCalc.Cli (fincalc.exe)

- 引数 → 計算 → **stdout に JSON 1行**。エラーは **stderr + exit 2**
  (PAD が `StandardOutput=>CliOut StandardError=>CliErr ExitCode=>CliExit` で受ける前提)
- コマンド: `dep schedule` / `tax invoice|withholding|corporate` /
  `excel dep-batch|invoice|read|sample` / `mcp` (JSON-RPC over stdio)
- 計算失敗でも部分結果を JSON で返す設計 (バッチ系は okCount/errorCount)

### 3.4 PAD テンプレート (pad/recipes → pad/*.txt)

- ソースは `pad/recipes/*.pad` (padkit レシピ)。生成物 `pad/*.txt` は編集禁止
- 環境値は `pad/profiles/*.json` に集約 (`#! requires:` キーが SET 行に展開される)
  - `client.json` = 配布先用サンプル (`C:\work\fincalc`) /
    `dev.json` = 本機 (gitignore。`dev.json.example` からコピー)
  - `dev.local.json` = 秘密値オーバーレイ (gitignore。`--profile dev --profile dev.local` で後勝ち)
- 15 テンプレートの用途は `pad/README.md` の一覧表参照

### 3.5 padkit designer (検証自動化)

- padkit の `designer` サブコマンド (FlaUI / UIA3) で PAD を外から操作する
- PAD 11.2609 の AutomationId は実測で固定済み
  (`ProgramItemsListBoxActions`, `ErrorCountTextBlock`, `Flow_status_*` 等。
  一覧は repo ルートの README / docs/design.md 参照)

## 4. インターフェース仕様

### 4.1 CLI ↔ PAD 契約

- `exit 0` + stdout=JSON → 成功。`exit 2` + stderr=日本語エラー → 計算/引数エラー
- PAD 側の定型ブロック (`run-cli-or-exit`): exit≠0 ならダイアログ→EXIT
- JSON は全コマンド snake_case のフラットなオブジェクト
  (`dep schedule` は `schedule[]` に year/method/rate/expense/accumulated/bookValueEnd/isRevised)

### 4.2 PAD テキスト形式 (PAD 11.2609 実機検証済みの制約)

| 制約 | 正しい形 |
|---|---|
| アクション識別子 | `モジュール.アクション.バリアント` 3セグメント (`Scripting.RunDOSCommand.RunDOSCommand`)。例外あり (padkit rules `twoSegmentAllow`) |
| アイコン | `Display.Icon.Error` は解析不能 → `Warning` を使う |
| SET の式値 | `%` なし (`SET X TO G.rate * 100`)。`%var%` は `$'''...'''` 内のみ |
| `%%` | リテラル `%` のエスケープ (URL 等) |
| エラーブロック | `ON BLOCK ERROR` は貼付不可 → `LOOP WHILE`+`IF`+`WAIT` リトライ |
| FOREACH | `LoopIndex` は生成されない → カウンタ変数 |
| IF 条件 | enum 参照不可 (`Display.DialogResult.Yes` → `'Yes'` 文字列比較) |

これらは padkit `rules/pad-11.2609.json` として機械検査可能な形で保持。

### 4.3 Teams Workflows webhook

- 発行: チャネル「…」→ ワークフロー → 「Webhook アラートをチャネルに送信する」
  (UI Automation で実機作成・検証済み。旧 Incoming Webhook コネクタは新 Teams 不可)
- **Adaptive Card 必須**。`{"text":"..."}` は 202 を返してもサイレント廃棄される
- 応答は HTTP 202 → `200 <= WebStatus <= 299` を成功判定
- `EncodeRequestBody: False` 必須 (`True` だと JSON が URL エンコードされて壊れる)
- URL 中の `%` は `%%` にエスケープ (`%` は PAD 変数記法と衝突)
- カード JSON 内にバックスラッシュ入り変数 (`%OutputXlsx%` 等) を埋め込まない
  → `\U` 等が不正エスケープになり同様にサイレント廃棄される。
  stderr も直接埋め込まず終了コードのみ載せる
- URL は sig 付き秘密情報。コミット側はプレースホルダ、実値は gitignore 対象
  (`profiles/dev.local.json` / `run/`) のみ

## 5. 品質保証

| 層 | 手段 |
|---|---|
| 計算 | xunit 21 件。国税庁公表の計算例と突合 (償却費・源泉税額等) |
| レシピ → テキスト | padkit lint (rules/pad-11.2609.json) + GoldenTests (15 レシピ全件 0 指摘) |
| PAD 適合 | `padkit designer check` — 実機デザイナーに貼付し actions>0 & errors=0 |
| 実行 | `padkit designer run` — 保存→実行→`Flow_status_ready` 遷移を監視 |
| Teams | 実チャネル投稿を UIA で確認 (08-15 全件 投稿済) |

## 6. 実行環境への持ち込みフロー

1. fincalc.exe を `dotnet publish -r win-x64 --self-contained` で単体 exe 化して持ち込み
   (または .NET SDK 環境で `dotnet build`)
2. `profiles/client.json` を配布先のパス/WebhookUrl に編集
3. `padkit render-all recipes --profile client.json --out-dir pad`
4. PAD デザイナーに `pad/*.txt` を貼り付け (手動 or `padkit designer check/paste`)
5. 実行前に `padkit lint` + `designer check` で検査

## 7. 既知の制約と拡張ポイント

- Workflows webhook は一方向 — 承認ボタンの応答をカードから直接受け取れない。
  13 は「依頼カード→ローカル Yes/No→結果カード」で代替。真の承認が要るなら
  Power Automate クラウドの承認アクションを足す
- 02 (PAD 側 Excel 行ループ) は Excel 必須。無い環境は 07 (exe 直読み) を使う
- UIA は稀に COMException (0x800705B4 等) で落ちる → 呼び出し側でリトライ
- 拡張候補: SharePoint 出力 + 共有リンクカード / 定期実行トリガー /
  メンション付き承認 / 複数チャネル同報
