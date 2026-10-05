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

責務ごとのクラス構成。拡張は「新しいクラスを足す」だけで既存コードを触らない。

- `Depreciation/IDepreciationStrategy` — 償却方法1種類ぶんのスケジュール生成。
  実装: `StraightLineStrategy` (定額法) / `DecliningBalance200Strategy`
  (200%定率法、償却保証額で改定償却率へ切替・均等償却)
- `DepreciationStrategies` — 方法→戦略のレジストリ。`With(strategy)` で
  部署独自の償却方法を差し込んだ別セットを作れる
- `DepreciationCalculator` — 入力検証 + 戦略解決の窓口。
  `DepreciationCalculator.Default` が既定構成の共有インスタンス
- `Tax/*Calculator` — `ConsumptionTaxCalculator` (10%/軽減8%,
  インボイス方式=税率別合計→端数処理)、`WithholdingTaxCalculator`
  (10.21% / 100万円超分 20.42%)、`CorporateTaxCalculator` (概算)。
  いずれもインスタンスクラスで、バッチ・CLI から注入される
- `RateTable` — 耐用年数省令の償却率表 (定額法=算出式、定率法=別表第十)
- 計算例は国税庁公表値との突合を xunit で回帰 (`tests/FinCalc.Tests` 24 件)

### 3.2 FinCalc.Excel (ClosedXML 0.105.1, Excel 本体不要)

- `SheetReader` — シート走査。先頭10行以内から既知ヘッダ列を探索
  (タイトル行付きの複雑な帳票を吸収)。ヘッダ行は `--header-row` で手動指定可
- `ColumnMap` — 「論理名 → 許容ヘッダ名」の対応表を保持するインスタンス。
  `ColumnMap.Asset` / `ColumnMap.Invoice` が既定、`WithOverrides` で差分合成
- `ColumnMapSet.Load(path)` — 部署別の列対応 JSON を読む
  (例: `columns/asset-columns.example.json`)。部署ごとにヘッダ名が
  違っても JSON を配るだけでコード変更不要。CLI では `--columns` で指定
- `HeaderMap` — 既定エイリアス表 + 値パーサ (金額/整数/償却方法/端数処理)
- `DepreciationBatchProcessor` — `excel dep-batch` の本体。
  `ColumnMap` と `DepreciationCalculator` をコンストラクタ注入。
  全シート横断→行ごと償却→summary(結果+error列)と schedule シート出力
- `InvoiceBatchProcessor` — 同型で請求集計 (`ConsumptionTaxCalculator` 注入)
- `DepBatch` / `InvoiceBatch` — 既定構成の静的ファサード (後方互換)
- `SampleWorkbook` — サンプル生成

### 3.3 FinCalc.Cli (fincalc.exe)

- `Commands/ICommand` — サブコマンド1件の責務 (`Name` / `Usage` / `Run`)。
  責務ごとのサブシナリオとして `Commands/` 配下に1機能1クラス
- `CommandArgs` — `--key value` パース済み引数 + 共通バリデーション
  (`Req/ReqInt/ReqRate/OptRound/LoadColumns`)
- `CommandRegistry` — コマンド名→実装の解決。`With(command)` で拡張可能
- `Program.cs` — 引数パースと `Resolve→Run→stdout JSON` のみを行う薄い層
- `exit 0` + stdout=JSON → 成功。`exit 2` + stderr → 計算/引数エラー
  (PAD が `StandardOutput=>CliOut StandardError=>CliErr ExitCode=>CliExit` で受ける前提)
- `mcp` コマンドは MCP stdio サーバー (`McpTools`) として起動
- 計算失敗でも部分結果を JSON で返す設計 (バッチ系は okCount/errorCount)

### 3.4 PAD テンプレート (pad/recipes → pad/*.txt)

- ソースは `pad/recipes/*.pad` (padkit レシピ)。生成物 `pad/*.txt` は編集禁止
- **責務分割**: `#! subflow <名>` セクションで責務ごとに分離 (16 番が雛形)。
  生成物は `名.txt` (Main) + `名.<サブフロー>.txt` に分割。PAD はサブフロー
  定義をテキスト貼り付けできないため `padkit designer flow <main.txt>` が
  タブ作成→本文貼付→Main 貼付→全体検査まで行う。サブフローは引数を持たず
  変数はフロー全体共有 — 共有変数 (CliExit/Result/WebhookUrl) が事実上の
  「引数/戻り値」として機能する設計
- 環境値は `pad/profiles/*.json` に集約 (`#! requires:` キーが SET 行に展開される)
  - `client.json` = 配布先用サンプル (`C:\work\fincalc`) /
    `dev.json` = 本機 (gitignore。`dev.json.example` からコピー)
  - `dev.local.json` = 秘密値オーバーレイ (gitignore。`--profile dev --profile dev.local` で後勝ち)
- 16 テンプレートの用途は `pad/README.md` の一覧表参照

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
| サブフロー | `FUNCTION` ブロックは貼付不可 → `#! subflow` で `名.<Sub>.txt` を生成し `designer flow` でタブごと貼付。呼び出しは `CALL <名>` |

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
| 計算 | xunit 24 件。国税庁公表の計算例と突合 (償却費・源泉税額等) + 列対応・戦略拡張の回帰 |
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
