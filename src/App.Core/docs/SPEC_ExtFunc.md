# App.Core 外部機能仕様書

本ドキュメントは `src/App.Core` プロジェクトの公開 API 仕様（外部機能仕様）を記載します。
メソッド内部のコードは記載しません。外部利用者に関係する情報のみを扱います。

## 目次 <!-- omit from toc -->

- [1. 公開型一覧](#1-公開型一覧)
  - [1.1 列挙型（`EsUtil.ZenHanConverter.App.Core.Enums`）](#11-列挙型esutilzenhanconverterappcoreenums)
    - [1.1.1 `ZenHanMode`](#111-zenhanmode)
    - [1.1.2 `ZenHanKanaMode`](#112-zenhankanamode)
    - [1.1.3 `ZenHanEtcZenHanAsciiMode`](#113-zenhanetczenhanasciimode)
    - [1.1.4 `ZenHanEtcYenMode`](#114-zenhanetcyenmode)
    - [1.1.5 `ZenHanEtcSpecial`](#115-zenhanetcspecial)
  - [1.2 データモデル（`EsUtil.ZenHanConverter.App.Core.Models`）](#12-データモデルesutilzenhanconverterappcoremodels)
    - [1.2.1 `ConvertConfig`](#121-convertconfig)
    - [1.2.2 組み込みプリセットの内容](#122-組み込みプリセットの内容)
    - [1.2.3 `ModePropDef`](#123-modepropdef)
    - [1.2.4 `ReplacePair`](#124-replacepair)
    - [1.2.5 `SegmentItem` / `SegmentDefine`](#125-segmentitem--segmentdefine)
    - [1.2.6 `SegmentDefinitions`](#126-segmentdefinitions)
    - [1.2.7 `AppSetting`](#127-appsetting)
    - [1.2.8 `SettingsPersistenceBase<TSettings>`](#128-settingspersistencebasetsettings)
  - [1.3 インターフェース（`EsUtil.ZenHanConverter.App.Core.Interfaces`）](#13-インターフェースesutilzenhanconverterappcoreinterfaces)
    - [1.3.1 `IClipboardService : IDisposable`](#131-iclipboardservice--idisposable)
    - [1.3.2 `ITextConverter`](#132-itextconverter)
  - [1.4 変換ロジック（`EsUtil.ZenHanConverter.App.Core.Logic`）](#14-変換ロジックesutilzenhanconverterappcorelogic)
    - [1.4.1 `CharConverter : ITextConverter, IDisposable`](#141-charconverter--itextconverter-idisposable)
  - [1.5 ウィンドウ位置補正（`EsUtil.ZenHanConverter.App.Core.Helpers`）](#15-ウィンドウ位置補正esutilzenhanconverterappcorehelpers)
    - [1.5.1 `WindowPlacement`](#151-windowplacement)
    - [1.5.2 `SplitLayout`](#152-splitlayout)
  - [1.6 変更検出（`EsUtil.ZenHanConverter.App.Core.Logic`）](#16-変更検出esutilzenhanconverterappcorelogic)
    - [1.6.1 `ClipboardChangeDetector`](#161-clipboardchangedetector)
  - [1.7 ウィンドウの復元（`EsUtil.ZenHanConverter.App.Core.Native`）](#17-ウィンドウの復元esutilzenhanconverterappcorenative)
    - [1.7.1 `ScreenVisibleArea`](#171-screenvisiblearea)
  - [1.8 アイコンデータ（`EsUtil.ZenHanConverter.App.Core.Icons`）](#18-アイコンデータesutilzenhanconverterappcoreicons)
    - [1.8.1 `FluentIconData`](#181-fluenticondata)

## 1. 公開型一覧

### 1.1 列挙型（`EsUtil.ZenHanConverter.App.Core.Enums`）

#### 1.1.1 `ZenHanMode`

全角/半角変換の基本方向。

| 値 | 名称 | 説明 |
| --- | --- | --- |
| 0 | None | 変換なし |
| 1 | ToHan | 全角文字を半角に変換 |
| 2 | ToZen | 半角文字を全角に変換 |

#### 1.1.2 `ZenHanKanaMode`

かな文字（半角カナ/全角カタカナ/全角ひらがな）の変換方向。

| 値 | 名称 | 説明 |
| --- | --- | --- |
| 0 | None | 変換なし |
| 1 | ToHan | 全角かなを半角カナへ |
| 2 | ToZenKata | 半角カナ/全角ひらがなを全角カタカナへ |
| 3 | ToZenHira | 半角カナ/全角カタカナを全角ひらがなへ |

#### 1.1.3 `ZenHanEtcZenHanAsciiMode`

かな約物（長音/読点/句点）の変換モード。

| 値 | 名称 | 説明 |
| --- | --- | --- |
| 0 | None | 変換なし |
| 1 | ToHan | 全角を半角へ |
| 2 | ToZen | 半角を全角へ |
| 3 | ToAscii | 対応する ASCII 文字へ（例: ー → -） |

#### 1.1.4 `ZenHanEtcYenMode`

円記号/バックスラッシュの変換モード。

| 値 | 名称 | 説明 |
| --- | --- | --- |
| 0 | None | 変換なし |
| 1 | ToHanBSlash | 半角バックスラッシュ（\）へ |
| 2 | ToZenBSlash | 全角バックスラッシュ（＼）へ |
| 3 | ToHanYen | 半角円記号（¥）へ |
| 4 | ToZenYen | 全角円記号（￥）へ |

#### 1.1.5 `ZenHanEtcSpecial`

特殊文字（タブ/改行/連続スペース）の変換モード。

| 値 | 名称 | 説明 |
| --- | --- | --- |
| 0 | None | 変換なし |
| 1 | Remove | 除去 |
| 2 | ToHanSpace | 半角スペース |
| 3 | ToZenSpace | 全角スペース |

### 1.2 データモデル（`EsUtil.ZenHanConverter.App.Core.Models`）

#### 1.2.1 `ConvertConfig`

全角/半角変換の全設定を管理します。`SettingsPersistenceBase<ConvertConfig>` を継承し、JSON への自動保存に対応します。

主要な公開メンバー:

- `bool IsEnabledZenHan` — 全角/半角変換の有効/無効（既定 `false`）。MewUI 版・WinUI 3 版とも参照せず、全角/半角変換は常に有効です（設定ファイル上の値として保持し、プリセット適用時に `true` になります）
- 各種 `ConvertMode*` プロパティ — 文字カテゴリごとの変換モード
- `List<ReplacePair> ReplacePairs` — ユーザー定義の置換ルール一覧
- `string[] GetPresetNames()` — 利用可能なプリセット名一覧
- `void SavePreset(string name)` / `bool LoadPreset(string name)` / `void DeletePreset(string name)`
- `static string PresetDirectory` — ユーザープリセットの保存ディレクトリ（既定 `%LOCALAPPDATA%\ZenHanConverter\Presets`、既定値は設定で切替可能）
- `bool IsBuiltInPreset(string name)`
- `string? FindMatchingPreset()` — 現在の設定に一致するプリセット名を返す
- `void ExportToFile(string filePath)` / `bool ImportFromFile(string filePath)`
- 組み込みプリセット名の定数 `BuiltInPresetAccountingPower` / `BuiltInPresetAlphanumericHanKanaZen`
- `static class Mode` — 全モードプロパティの型安全なアクセサ定義（`ModePropDef`）を公開。UI は文字列名ではなく `ModePropDef` を直接参照する（NativeAOT 対応）

#### 1.2.2 組み込みプリセットの内容

| プリセット名 | 数値・英字 | 記号 | かな | その他 |
| --- | --- | --- | --- | --- |
| 全力会計（Built-in） | 半角 | `()` `-` `/` `,` `.` 空白は半角、それ以外の記号は全角 | 半角カナ→全角カタカナ。全角カタカナ/ひらがなは変換なし | バックスラッシュ/円記号→半角円記号、タブ/改行/連続スペース→半角スペース |
| 英数記号半角、かな全角（Built-in） | 半角 | 全て半角 | 半角カナ→全角カタカナ。全角カタカナ/ひらがなは変換なし | かな約物は全角、改行は変換なし、タブ/連続スペース→半角スペース |

いずれも適用時に `ReplacePairs` を空にし、`IsEnabledZenHan` を `true` にします。

#### 1.2.3 `ModePropDef`

`ConvertConfig` のモードプロパティへ型安全にアクセスする `record`。

| プロパティ | 型 | 説明 |
| --- | --- | --- |
| Get | `Func<ConvertConfig, object>` | 現在値を取得するデリゲート |
| Set | `Action<ConvertConfig, object>` | 値を設定するデリゲート |
| ToZenValue | `object?` | 「全力会計」プリセットで全角にする値（null は全角設定の対象外） |

#### 1.2.4 `ReplacePair`

ユーザー定義の文字列置換ペア（`record`）。

| プロパティ | 型 | 説明 |
| --- | --- | --- |
| Search | string | 置換元（正規表現可） |
| Replace | string | 置換先 |
| IsRegex | bool | 正規表現として扱うか |

- `static bool TryValidate(string search, string replace, bool isRegex, out string? errorMessage)` — 検証

#### 1.2.5 `SegmentItem` / `SegmentDefine`

UI セグメントコントロールの定義レコード。

- `SegmentItem(string Content, object Value, bool IsEnabled = true)` — 個別セグメント（表示テキスト / 値 / 有効状態）
- `SegmentDefine(string Label, ModePropDef Mode, double Height = NaN, SegmentItem[]? Segments = null, bool? ForceEnableState = null)` — 1 変換項目の定義。`Mode` でバインド先モードを型安全に参照

#### 1.2.6 `SegmentDefinitions`

設定画面の変換カテゴリ定義を提供する静的クラス。UI フレームワークに依存しないため Core に配置しています。

| メンバー | 型 | 説明 |
| --- | --- | --- |
| `NumberDefs` / `AlphabetDefs` | `SegmentDefine[]` | 数字・英字の変換項目 |
| `KanaDefs` | `SegmentDefine[]` | かな（半角カナ / 全角カタカナ / 全角ひらがな）の変換項目 |
| `SymbolDefs` | `SegmentDefine[]` | 記号（括弧 / 引用符 / 演算子）とかな記号の変換項目 |
| `EtcZenHanAsciiDefs` | `SegmentDefine[]` | かな約物（長音 / 読点 / 句点）の変換項目 |
| `EtcBslashYenDefs` | `SegmentDefine[]` | バックスラッシュ / 円記号の変換項目 |
| `EtcSpecialDefs` | `SegmentDefine[]` | 特殊文字（タブ / 改行）の変換項目 |
| `EtcMultiSpaceDefs` | `SegmentDefine[]` | 連続スペースの変換項目 |

各項目はバインド先を `ConvertConfig.Mode`（`ModePropDef`）で参照するため、実行時のプロパティ名解決を行いません。

#### 1.2.7 `AppSetting`

アプリケーションのウィンドウ設定とクリップボード連携設定。`SettingsPersistenceBase<AppSetting>` を継承。

- `double WindowWidth` / `double WindowHeight` — ウィンドウサイズ（DIP）
- `double? WindowX` / `double? WindowY` — ウィンドウ左上の位置（画面座標・DIP、null は未保存）
- `bool IsClipboardConvertEnabled` — クリップボード変換（コピーの検知・変換結果の書き戻し）の有効/無効。false の場合はクリップボードを読み書きしません

#### 1.2.8 `SettingsPersistenceBase<TSettings>`

設定の JSON ファイルへの自動永続化を提供する基底クラス（`AppSetting` / `ConvertConfig` が継承）。

| メンバー | 型 | 説明 |
| --- | --- | --- |
| `Initialize()` | `void` | 設定ファイルを読み込み、自動保存を開始する |
| `IsAutoSave` | `bool` | 自動保存の有効/無効 |
| `AutoSaveFileName` | `string` | 自動保存先のファイルパス |
| `CancelPendingSave()` | `void` | 保留中の自動保存を取り消す |
| `SaveNow()` | `void` | 保留中の自動保存を取り消し、現在の内容を自動保存先へ**同期で**保存する |
| `SaveToJsonFile(string filePath)` | `void` | 指定パスへ同期で保存する |
| `LoadFromJsonFile(string filePath)` | `void` | 指定パスから読み込んで適用する |

`SaveNow` はアプリの終了時など、デバウンス（300ms）の完了を待てない場面で使用します。
終了直前はプロセスが停止するため、非同期保存では書き込みが完了しません。

### 1.3 インターフェース（`EsUtil.ZenHanConverter.App.Core.Interfaces`）

#### 1.3.1 `IClipboardService : IDisposable`

- `event EventHandler<object>? ContentChanged` — クリップボード変更イベント
- `Task<string?> GetTextAsync()` — テキスト取得
- `void SetText(string text)` — テキスト設定
- `void Flush()` — フラッシュ（永続化）

#### 1.3.2 `ITextConverter`

- `string Convert(string text)` — テキスト変換

### 1.4 変換ロジック（`EsUtil.ZenHanConverter.App.Core.Logic`）

#### 1.4.1 `CharConverter : ITextConverter, IDisposable`

- `ConvertConfig Config { get; }`
- `string Convert(string text)` — 設定に基づく全角/半角変換とユーザー定義置換
- `ConvertPairs GetConvertPairs()` — 現在の設定に基づく変換ペア生成（キャッシュ）

変換の実行可否（`ConvertConfig.IsEnabledZenHan`）は**参照しません**。変換をスキップするか否かは呼び出し側（画面側）のポリシーです。現在の両アプリ（MewUI 版・WinUI 3 版）は全角/半角変換を常に有効として扱います。

### 1.5 ウィンドウ位置補正（`EsUtil.ZenHanConverter.App.Core.Helpers`）

UI フレームワークに依存しないため Core に配置しています。座標は DIP の幾何型で扱います
（型は `EsUtil.Platform.Core.Geometry` の `EsPoint` / `EsSize` / `EsRect` を使います）。

#### 1.5.1 `WindowPlacement`

| メンバー | 型 | 説明 |
| --- | --- | --- |
| `ClampToVisibleArea(EsPoint saved, EsSize windowSize, EsRect visibleArea)` | `EsPoint` | 保存されたウィンドウ位置を表示領域内へ補正して返す |

補正内容は次のとおりです。

- 一部が表示領域外: はみ出した辺を表示領域の内側へ詰める
- 完全に表示領域外: 原点（プライマリモニタの左上）へ移動
- ウィンドウが表示領域より大きい: その軸の表示領域先頭へ寄せる

#### 1.5.2 `SplitLayout`

上下 2 分割レイアウトの分割比率を扱う純粋ロジック。MewUI 版を除く各 UI のホーム画面が共有します
（MewUI 版はフレームワーク標準の `SplitPanel` が分割を担うため参照しません）。

| メンバー | 型 | 説明 |
| --- | --- | --- |
| `DefaultRatio` | `double`（const） | 既定の分割比率（0.5） |
| `MinRatio` / `MaxRatio` | `double`（const） | 分割比率の下限 0.1 / 上限 0.9 |
| `ClampRatio(double ratio)` | `double` | 比率を有効範囲へ補正する（NaN は既定値） |
| `RatioFromDrag(double startRatio, double deltaY, double trackHeight)` | `double` | ドラッグ量から新しい分割比率を求める |
| `TopWeight(double ratio)` / `BottomWeight(double ratio)` | `double` | 上ペイン / 下ペインの割合（合計 1.0）を求める |

比率（0.0〜1.0）で表すため、UI 側は「上ペイン = 比率」「下ペイン = 1 - 比率」のスターサイズ（相対サイズ）へ
割り当てるだけでよく、**ウィンドウの高さが変化しても上下の比率が自動的に維持されます**。

`trackHeight` は上下 2 ペインの合計高さとし、分割バーの太さは含めません。

### 1.6 変更検出（`EsUtil.ZenHanConverter.App.Core.Logic`）

#### 1.6.1 `ClipboardChangeDetector`

クリップボードのシーケンス番号を監視し、内容の変更を検出します。UI フレームワーク・タイマーに依存しないため、ポーリング間隔は呼び出し側が決めます。

| メンバー | 型 | 説明 |
| --- | --- | --- |
| `ClipboardChangeDetector(Func<uint> readSequence)` | コンストラクター | シーケンス番号の読み取り関数を指定。構築時の値が初期値になる |
| `HasChanged()` | `bool` | 前回の確認から変化していれば true を返し、内部状態を更新する |
| `RecommendedPollIntervalMs` | `int`（const） | 推奨するポーリング間隔（250 ミリ秒）。各 UI のクリップボード監視タイマーが共有する単一所有元 |

### 1.7 ウィンドウの復元（`EsUtil.ZenHanConverter.App.Core.Native`）

UI フレームワークに依存しないため Core に配置しています。Win32 の P/Invoke は `EsUtil.Platform.Core` が単一所有します。

#### 1.7.1 `ScreenVisibleArea`

仮想画面（全モニタの外接矩形）に基づくウィンドウ位置の補正を提供します。
`EsUtil.Platform.Core` の `WinVirtualScreen`（物理ピクセルの取得）と `WindowPlacement`（補正ロジック）を
組み合わせ、各 UI のウィンドウ復元処理から重複を排除します。 DPI スケールの正規化は
`EsUtil.Platform.Core.Coord.DpiScaler.NormalizeScaling` が単一所有します。

| メンバー | 型 | 説明 |
| --- | --- | --- |
| `GetVisibleArea(double scale)` | `EsRect` | 仮想画面の外接矩形を DIP で取得。`scale` が 0 以下なら 100% とみなす |
| `Clamp(EsPoint saved, EsSize windowSize, double scale)` | `EsPoint` | 保存されたウィンドウ位置を表示領域内へ補正して返す |
| `ToPhysical(double dip, double scale)` | `int` | DIP を物理ピクセルへ換算（四捨五入）。丸め規則を一元管理する |
| `ToDip(int physical, double scale)` | `double` | 物理ピクセルを DIP へ換算 |

### 1.8 アイコンデータ（`EsUtil.ZenHanConverter.App.Core.Icons`）

UI フレームワークに依存しないため Core に配置しています。SVG ファイルをアイコン形状の唯一の定義元とし、`Icons` フォルダの SVG を埋め込みリソースとして登録します。

#### 1.8.1 `FluentIconData`

Fluent Icons の SVG から抽出したパスデータ（`<path>` の `d` 属性の値）を提供します。形状の生成は各 UI フレームワーク側で行います（MewUI は `PathGeometry.Parse`、WinUI3 は `Geometry.Parse` など）。

| メンバー | 型 | 説明 |
| --- | --- | --- |
| `ConvertRange` | `string` | ホーム項目用「Convert Range」アイコンの SVG パスデータ（24px グリッド） |
| `Settings` | `string` | 設定項目用「Settings」アイコンの SVG パスデータ（24px グリッド） |

読み込みは初回アクセスの一度だけ行い、以降は抽出済みの文字列を返却します。
