# padkit 設計書

> 対象: PAD 11.2609.183.0 / .NET 8。変更時はこの文書も同じ PR で更新すること。

## 1. 目的と非目的

**目的**
- PAD フローを「貼り付け可能なテキスト」として資産化し、レシピ (`.pad`) +
  プロファイル (JSON) から環境ごとのテキストを再生成できるようにする
- PAD バージョン依存の構文罠を、貼り付ける**前に** lint で検出する
- 実行中のデザイナーへのクリア → 貼り付け → エラー数取得 → 保存 → 実行を自動化し、
  AI 補助のない環境でも同じ手順で検証できるようにする

**非目的**
- PAD フローそのもののエンコーディング形式 (solution export) やクラウド
  Power Automate の API は扱わない。対象はあくまで「デザイナーへ貼り付ける
  テキスト形式」
- PAD のバージョン全般を網羅する抽象化はしない。規則は `rules/pad-<version>.json`
  に閉じ込め、別バージョンは別ルールセットとして育てる

## 2. 全体構成

```
┌─ recipes/*.pad ─┐      ┌─ profiles/*.json ─┐
│  PAD text       │      │  環境値 (後勝ち    │
│  + #! directives│      │  オーバーレイ)     │
│  + {{> block}}  │      └────────┬───────────┘
└───────┬─────────┘               │
        ▼                         ▼
┌─────────────────────────────────────────────┐
│ PadKit.Flow (net8.0, 依存なし)               │
│  Renderer   recipe → プレーンテキスト         │
│  Linter     データ駆動規則 + 構造規則         │
│  Profile / BlockResolver / RuleSet           │
└──────────┬──────────────────────┬───────────┘
           │                      │
┌──────────▼──────────┐  ┌────────▼─────────────┐
│ PadKit.Designer     │  │ rules/pad-11.2609.json│
│ (net8.0-windows)    │  │ blocks/*.pad          │
│ FlaUI UIA3          │  └──────────────────────┘
│ PadDesigner         │
└──────────┬──────────┘
           ▼
┌──────────────────────┐
│ PadKit.Cli (padkit)  │  render / render-all / lint / blocks / designer
└──────────────────────┘
```

### 各層の責務
- **PadKit.Flow**: Windows に依存しない純粋な生成・検査。テスト可能な全ロジックはここ
- **PadKit.Designer**: UIA3 越しのデザイナー操作。画面解釈 (AutomationId) をこの
  1 クラスに隔離し、CLI は詳細を知らない
- **PadKit.Cli**: 引数パース・終了コード・出力形式だけの薄い層
- **blocks/**: PAD テキストの部品。アプリ非依存の汎用部品のみ置く
- **rules/**: バージョンごとの lint 規則セット

## 3. テンプレート言語 (.pad)

設計方針は「PAD テキストそのまま + 最小のディレクティブ」。非エンジニアが
生成物とレシピを読み比べたときに対応が一目で追えることを優先し、
抽象度の高い DSL (YAML でフローを組む等) は採らなかった。

### 3.1 ディレクティブ `#!`
先頭コメント部 (連続する `#` 行) の中だけで意味を持つ。

- `#! requires: Key1 Key2` — プロファイルから SET ヘッダを自動挿入。
  文字列値は `$'''...'''` で包み `\` を二重化、数値/真偽値は生値。
  不足キーは**まとめて**エラー (E_PROFILE)。PAD 側から見れば
  「先頭に SET が並ぶ普通のフロー」にしかならないため、生成物だけを
  読んでも追従できる

### 3.2 インクルード `{{> name k="v"}}`
- 独立行のみ。行頭空白は全出力行へ継承 → `IF`/`LOOP` 内でも正しくネスト
- 引数の値評価は**パース後置換**: `"..."` の中身を取り出してから `{{param}}`
  を展開する。逆順だと値中の `"` が引数区切りを壊す
- 再帰 include は深さ 8 で打ち切り (循環検出)
- ブロック宣言 `#! param X` (必須) / `#! param X default="..."`。未知引数・
  未指定必須引数は名前を並べてエラー

### 3.3 プロファイルとオーバーレイ
- フラットな JSON オブジェクト 1 枚。`--profile` 複数回指定で**後勝ちマージ**
- 用途: コミット済み `dev.json` + gitignore 済み `dev.local.json` で
  秘密値 (Webhook URL) だけローカル差し替え

### 3.4 未解決参照
展開後に残った `{{` は E_UNRESOLVED エラー。暗黙の変数を通さず、
タイプミスを必ず render 時に失敗させる。

## 4. lint モデル

### 4.1 2 系統の規則
- **データ駆動** (`rules/*.json`): `{id, severity, scope, pattern, message, fix}`。
  規則追加が「JSON に 1 件書く」で済む → 新バージョン対応や実機で見つけた
  罠の取り込みが PR 1 行になる
- **構造規則** (C#): PAD100 (IF/LOOP/ELSE/END 対応) / PAD101 (未解決 `{{`)。
  正規表現では書けない文脈依存の検査

### 4.2 scope と文字列マスク
`$'''...'''` は複数行に跨る PAD 固有リテラル。全文に対して「文字列内部か」を
示すビットマスクを作り、`code` 規則は文字列内を空白化したテキスト、
`string` 規則は逆側を空白化したテキストへ適用する。これにより
「`$'''SET X TO %a%'''` (文字列) は PAD006 無対象、PAD007 対象」のような
見分けを行単位正規表現では壊れない形で実現している。

### 4.3 PAD002 の allowlist
3 セグメント必須だが `File.WriteText` `Variables.ConvertJsonToCustomObject`
等は 2 セグメントで受理される。実機で通ることが確認できたものだけを
`twoSegmentAllow` に入れ、機械的に「違反/例外」を管理する。
新しい 2 セグ動作を通したい場合は実機ペースト → 受理確認 → allowlist 追加、の順。

### 4.4 規則の正しさ
各ルールに positive/negative の xunit ケースを持つ (LinterTests)。
「検出する」だけでなく「正当な書き方を誤検知しない」ことを保証する
(PAD007 の `%%` エスケープ対応など、誤検知側のバグを何度か潰した経緯がある)。

## 5. デザイナー自動化 (PadDesigner)

### 5.1 要素特定
座標は一切使わず **AutomationId** で探す (version 11.2609.183.0 で dump 確認済み)。
座標は DPI/レイアウトで壊れるため、バージョン差異の診断として AutomationId も
dump できるよう `designer dump` コマンドを残している。

| AutomationId | 役割 |
|---|---|
| `ProgramItemsListBoxActions` | アクションキャンバス |
| `ProgramItemTemplateSummaryTextBlock` | アクション各行 (個数カウントの裏付け) |
| `ProgramDetailsStatusBarItem` | ステータスバー (アクション数) |
| `ErrorCountTextBlock` | エラー件数 |
| `Flow_status_*` | ready/running/parsing 等 |
| `SaveDraftFlowButton` / `StartFlowButton` | 保存 / 実行 |

### 5.2 実機で学んだ制約
- **FocusCanvas**: キャンバス中央は `EmptyState` オーバーレイがありフォーカスが
  取れない。上端から 80px (BoundingRectangle 算出) をクリックする。
  ListItem の BoundingRectangle は仮想化でキャンバス外を指すことがあり信用しない
- **Status()**: Save/Run 中に UIA が COMException で一時応答しないことがある。
  "unknown" を返してポーリング継続する
- **貼り付け**: Ctrl+V 後は ActionCount が 1.5s 変わらなくなるまで待つ
  (大きいフローはペーストが時間差で流れ込む)
- **run のガード**: `actions=0` でも errors=0 扱いで素通りすることがあるため
  「0 アクション」は errors>0 と同じ扱いで実行を拒否する

### 5.3 検証の 2 段階
PAD への貼り付け失敗は 2 種類あり、両方見ないと偽陰性になる:
1. **構文拒否** → 何も貼られない (actions=0)
2. **引数名違い** → 貼られるが赤枠 (errors>0)

`designer check` はこの両方を返す。lint は (1) を予防する位置づけ。

## 6. 決定記録 (簡易 ADR)

- **D1: JSON プロファイル、YAML なし** — `System.Text.Json` で依存ゼロ。
  `\\` エスケープが読みにくい欠点は「値は生パスで書き、レンダラーが
  二重化する」規約で緩和
- **D2: System.CommandLine 不使用** — 引数パースを自前で書き、CLI の依存を
  FlaUI のみに留めた。self-contained exe を配る前提でもサイズを小さく保つ
- **D3: テストは Flow 層のみ** — Designer 層は実機依存のため自動テスト対象外。
  代わりに `designer dump` で人手検査する手順を README に明記
- **D4: 生成物をリポジトリにコミットしない** — examples も recipes は
  commit、生成物は `out/` (gitignore)。ただし利用側 (fincalc) では
  「生成物を commit して PAD デザイナーで直接貼る」運用を許可した。
  どちらも生成物先頭の `do not edit` ヘッダで秩序を保つ

## 7. 拡張ポイント

- **新ルール**: `rules/*.json` へ 1 件 + LinterTests に正負 1 組
- **新ブロック**: `blocks/<name>.pad` + ゴールデンテストに key line 1 件
- **新 PAD バージョン**: `rules/pad-<ver>.json` を複製 → 差分を実機検証しながら直す
- **新 designer 操作**: PadDesigner に公開メソッドを 1 つ + CLI にサブコマンド
