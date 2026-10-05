# fincalc — 減価償却・税計算モック（PAD 連携用）

Power Automate Desktop (PAD) から使う金融計算のサンプル一式。
AI が使えない実行環境では、`fincalc.exe` を PAD の「DOS コマンドの実行」でキックし、
stdout の JSON を PAD 側で parse して使う構成を想定。

## 構成

```
apps/fincalc/
├── src/FinCalc/          計算ライブラリ（償却率表・減価償却・消費税・源泉・法人税）
├── src/FinCalc.Excel/    Excel 入出力層（ClosedXML。Excel 本体不要で xlsx を読み書き）
├── src/FinCalc.Cli/      fincalc.exe — PAD からキックする CLI
├── tests/FinCalc.Tests/  xunit テスト（国税庁公表の計算例と突合済み）
├── pad/                  PAD フロー雛形（生成物 *.txt。手編集しない）
│   ├── recipes/          レシピ（.pad = PAD テキスト + {{> block}} インクルード）
│   └── profiles/         環境別プロファイル JSON（client=配布先用サンプル / dev=本機用, gitignore）
└── samples/              フローが読む入力サンプル（CSV + assets.xlsx）
```

`pad/*.txt` は同 repo ルートの **padkit** で `recipes/*.pad` + プロファイルから生成する。
先に一度ビルドしておく:

```bash
dotnet build src/PadKit.Cli -c Release
PADKIT=src/PadKit.Cli/bin/Release/net8.0-windows/padkit.exe
# 全レシピを client プロファイルで一括レンダリング（pad/ 直下へ出力）
$PADKIT render-all apps/fincalc/pad/recipes \
    --profile apps/fincalc/pad/profiles/client.json --out-dir apps/fincalc/pad
# lint / 実機デザイナーへの貼り付け検証
$PADKIT lint apps/fincalc/pad/*.txt
$PADKIT designer check apps/fincalc/pad/0*.txt   # PAD デザイナー起動中に
```

テンプレートを直すときは `recipes/*.pad` を編集して render-all し直す
（生成先の `pad/*.txt` は直接編集しない）。環境値（パス/WebhookUrl）を変えたい
ときは `profiles/*.json` を編集する。

## こちらでのビルド・検証

```bash
cd apps/fincalc
dotnet build -c Release
dotnet test -c Release
dotnet run --project src/FinCalc.Cli -c Release -- dep schedule --method declining --cost 1000000 --life 10
```

## CLI コマンド一覧（stdout に 1 行 JSON、エラーは stderr + exit 2）

| コマンド | 例 |
|---|---|
| 償却スケジュール | `fincalc dep schedule --method straight\|declining --cost 1000000 --life 10 [--months 6] [--round floor\|ceiling\|nearest]` |
| 償却率参照 | `fincalc rates --life 10` |
| 消費税（税抜→税込） | `fincalc tax consumption --net 1000 --rate 10 [--round floor]` |
| 消費税（税込→税抜） | `fincalc tax consumption-net --gross 1100 --rate 10` |
| 請求書の税率別税額 | `fincalc tax invoice --lines '[{"net":333,"rate":10},{"net":111,"rate":8}]'` |
| 源泉所得税（報酬） | `fincalc tax withholding --amount 1500000` |
| 法人税概算（中小法人） | `fincalc tax corporate --income 10000000` |
| Excel 資産台帳一括 | `fincalc excel dep-batch --input assets.xlsx [--sheet 名\|番号\|*] [--header-row N] [--output out.xlsx]` |
| Excel 請求明細の税率別集計 | `fincalc excel invoice --input lines.xlsx [--sheet ...]` |
| Excel → JSON 化（PAD 用） | `fincalc excel read --input x.xlsx [--sheet 名\|番号]` |
| サンプル帳票生成 | `fincalc excel init-sample [--output assets.xlsx]` |

## Excel 一括処理（複雑な帳票向け）

`excel dep-batch` は Excel 本体不要で xlsx を読み、`summary`（資産ごとの集計＋error列）と
`schedule`（全資産の年度別償却明細）の 2 シートを持つ結果 xlsx を書き出す。

- **ヘッダ行は自動検出**: 先頭10行以内で既知のヘッダ列が最も多い行を採用。
  タイトル行・注記行があっても吸収する。明示したければ `--header-row N`。
- **列名エイリアス**: 資産名/asset/name、取得価額/cost、耐用年数/life、
  償却方法(定額/定率/straight/declining/db200)、供用月数/months、端数処理/round を認識。
- **シート指定**: `--sheet` に名前 or 1始まりの番号。`*` で全シート横断
  （既知ヘッダを持たないシートは自動で読み飛ばす）。
- **1行の失敗で止まらない**: エラーは summary の error 列に記録され、他の行は処理される。

## 実行環境への持ち込み手順

1. **exe の持ち込み**: `dotnet publish src/FinCalc.Cli -c Release -r win-x64 --self-contained` で
   単体 exe を作るか、対象環境に .NET SDK/ランタイムがあればソースごと持って `dotnet build`。
   持ち込みが難しければ PAD 側で同等計算を組むための仕様としてこのコードを参照する。
2. **PAD フローの再現**: `pad/*.txt` を開き、内容をコピー → 対象環境の PAD デザイナーで
   新規フローのアクションエリアに貼り付け。テキストがアクション列に復元される。
   - 環境ごとのパスは `pad/recipes/` + `pad/profiles/*.json` から `padkit render-all`
     で再生成する (`pad/README.md` 参照。`*.txt` は生成物で直接編集しない)。
   - 貼り付け〜検査〜実行の自動化は `padkit designer check/run` を使う
     (repo ルートの README 参照)。

### PAD テキスト形式の注意（PAD 11.2609.183.0 で貼り付け・実行まで実機検証済み）

テンプレートは以下のバージョン依存ルールに合わせてある。PAD が別バージョンなら
赤枠が出る場合がある（アクションを UI で開き直せば引数名が分かる）。
これらのルールは `padkit lint` (rules/pad-11.2609.json) で機械的に検査できる。

- アクション識別子は `モジュール.アクション.バリアント` の3セグメント:
  `Scripting.RunDOSCommand.RunDOSCommand`（`System.RunDOSCommand` は旧名で不可）、
  `File.ReadFromCSVFile.ReadCSV`、`Excel.LaunchExcel.LaunchAndOpen` など。
- メッセージボックスの Icon: `Display.Icon.Error` はこのバージョンで構文解析不能。
  有効なのは `None / Information / Question / Warning`（エラー表示は `Warning` で代用）。
- CSV 読込: 引数は `CSVFile:`（`CSVFilePath:` ではない）、enum は `File.CSVEncoding.UTF8` /
  `File.CSVColumnsSeparator.SystemDefault`、ヘッダ行スキップは `FirstLineContainsColumnNames: True`。
- `File.WriteText` の引数は `File:`（`FilePath:` ではない）。
- `SET` の式値は `%` で囲まない（`SET X TO G.rate * 100`）。`%var%` は `$'''...'''` 文字列内のみ。
- 文字列中で `%式%%` と直後に `%` を書くと全体がパース拒否される → 全角 `％` を使う。
- `ON BLOCK ERROR` はこのバージョンの貼り付けでは受理されない → 05 は `LOOP WHILE`+`IF`+`WAIT` で代替。
- `LOOP FOREACH` は `LoopIndex` を生成しない → 行番号が要る場合はカウンタ変数を +1 する（02 参照）。
- Excel 系: `ReadAllCells` は `FirstLineIsHeader`、閉じるのは `Excel.CloseExcel.CloseAndSave`。
  `System.RunApplication.RunApplication` に `Timeout` 引数は無い。

## Teams 連携（pad/08）

`08-teams-webhook-notify.txt` は計算結果を Teams チャネルに POST するテンプレート
（`Web.InvokeWebService.InvokeWebService` + Teams のワークフロー webhook。API キー不要）。

### Webhook URL の発行方法（UI Automation で実機作成確認済み）

1. 対象チャネルを開く → 右上「…」（その他のチャネル オプション）→「ワークフロー」
2. テンプレート「**Webhook アラートをチャネルに送信する**」を選択
3. チーム・チャネルを選んで「保存」→「Webhook リンクをコピー」で URL 取得

※ 旧「Incoming Webhook」コネクタは新 Teams では管理画面に出ないため、
  Workflows（Power Automate）方式が現行の正規ルート。

### 注意事項（実機検証済み）

- **本文は Adaptive Card 形式が必須**。`{"text":"..."}` は 202 受理されるが投稿されず
  サイレントに捨てられる。
- 応答は **HTTP 202**（受理）なので、ステータス判定は 200 以外の 2xx も成功扱いにする。
- `EncodeRequestBody` は必ず `False`（`True` だと JSON が URL エンコードされて壊れる）。
- URL に `%` を含む場合、テンプレート内では `%%` とエスケープする
  （`%` は変数記法と衝突する）。run/08 はエスケープ済みの実 URL で検証済み。
- Webhook URL は sig 付きの秘密情報なので、コミット対象のプロファイルには
  プレースホルダを入れてある。実 URL は `pad/profiles/dev.local.json`（gitignore 対象、
  `dev.local.json.example` をコピーして作成）に置き、`--profile dev.json
  --profile dev.local.json` の後勝ちオーバーレイで gitignore 対象の `run/` へ生成する。

`09-teams-error-alert.txt` は成否でカードを色分けするアラート型:

- 成功 → FactSet 付きの `good`（緑帯）カード
- 失敗 → `attention`（赤帯）カード。stderr は JSON 破壊文字を含み得るため
  カードには載せず `out/flow.log` に追記し、カードには終了コードだけ出す。
- Adaptive Card の `FactSet`/`Container.style`/`color` は Teams でそのまま描画される
  （実チャネルへの投稿で検証済み）。
3. **動作確認**: `samples/` の CSV を `in/` に置いて 06 のバッチフローを回す、
   または 01 の単発フローで JSON→CSV 出力を確認する。

## 計算の前提（税務の正確性メモ）

- 償却率表: 耐用年数省令 別表第八（定額法 = ceil(1/年, 3桁)）・別表第十（200%定率法、
  平成24年4月1日以後取得分）を収録。対象は耐用年数 **2〜86年**。87年以上が必要なら
  `RateTable.cs` に行追加。
- 定率法の改定判定は按分前の率で行う（省令5条4項）。償却費の円未満は既定で切捨て
  （国税庁公表例と一致）。`--round` で変更可。
- 消費税: 10% / 軽減8%。インボイスは税率ごとに合計してから1回だけ端数処理。
- 源泉所得税: 報酬・料金の 10.21% / 100万超 20.42%（復興特別所得税込み、1円未満切捨）。
- 法人税: 中小法人概算（800万以下15% / 超過23.2% + 地方法人税10.3%）。
  住民税・事業税は含まない概算値。

## 免責

モック・試算用。実務適用前に顧問税理士等で計算結果を検証すること。
クライアント名・案件情報はこのフォルダに書かないこと。
