# App.MewUI 外部機能仕様書

本ドキュメントは `src/App.MewUI`（MewUI アプリ）の公開 API 仕様（外部機能仕様）を記載します。
メソッド内部のコードは記載しません。外部利用者に関係する情報のみを扱います。

## 目次 <!-- omit from toc -->

- [1. 公開型一覧](#1-公開型一覧)
  - [1.1 ViewModels](#11-viewmodels)
    - [1.1.1 `HomeViewModel : IDisposable`](#111-homeviewmodel--idisposable)
  - [1.2 Views（`EsUtil.ZenHanConverter.App.MewUI.Views`）](#12-viewsesutilzenhanconverterappmewuiviews)
    - [1.2.1 `MainWindow : Window`](#121-mainwindow--window)
  - [1.3 Helpers（`EsUtil.ZenHanConverter.App.MewUI.Helpers`）](#13-helpersesutilzenhanconverterappmewuihelpers)
    - [1.3.1 `FluentIcons`](#131-fluenticons)
    - [1.3.2 `HomeView : UserControl`](#132-homeview--usercontrol)
    - [1.3.3 `SettingsView : UserControl`](#133-settingsview--usercontrol)
    - [1.3.4 `MultiColumnPanel : Panel`（`EsUtil.ZenHanConverter.App.MewUI.Views`）](#134-multicolumnpanel--panelesutilzenhanconverterappmewuiviews)
  - [1.4 Services（`EsUtil.ZenHanConverter.App.MewUI.Services`）](#14-servicesesutilzenhanconverterappmewuiservices)
    - [1.4.1 `ClipboardService : IClipboardService`](#141-clipboardservice--iclipboardservice)
    - [1.4.2 `INavigationService`](#142-inavigationservice)
    - [1.4.3 `NavigationService : INavigationService`](#143-navigationservice--inavigationservice)
- [2. エントリポイント](#2-エントリポイント)

## 1. 公開型一覧

### 1.1 ViewModels

設定画面の ViewModel は **共有プレゼンテーション層（`src/App.Core.Presentation`）** が提供します。
公開 API は `../../App.Core.Presentation/docs/SPEC_ExtFunc.md` を参照してください。

本アプリ固有の ViewModel は `HomeViewModel`（ホーム画面）です。

#### 1.1.1 `HomeViewModel : IDisposable`

ホーム画面のデータ管理、クリップボード監視と文字変換実行。

- `string BeforeText` — 変換前テキスト（クリップボード取り込みまたは手動入力。変更時に自動変換）
- `string ConvertedText` — 変換後テキスト
- 全角/半角変換は常に有効（オン/オフの切り替えはありません）。クリップボード変換（コピー検知・書き戻し）の有効/無効は `AppSetting.IsClipboardConvertEnabled` に従います。

### 1.2 Views（`EsUtil.ZenHanConverter.App.MewUI.Views`）

#### 1.2.1 `MainWindow : Window`

メインウィンドウ。カスタムタイトルバー（アプリ名・クリップボード変換スイッチ・プリセット選択・ウィンドウ操作ボタン）、ウィンドウサイズの復元/保存、NavigationView によるペイン式ナビゲーションとコンテンツ表示。

- カスタムタイトルバーには「ZenHanConverter」、クリップボード変換の有効/無効スイッチ、プリセット選択ドロップダウン、ウィンドウ操作ボタンを表示します。
- ウィンドウ操作ボタンは最小化・最大化（最大化中は復元）・閉じるの 3 つで、それぞれ `Window` のウィンドウ操作を呼び出します。
- 起動時は `AppSetting.WindowWidth` / `WindowHeight`（サイズ）と `WindowX` / `WindowY`（位置）を復元し、終了時は現在の値（最大化/最小化中は通常状態での最後の値）を `AppSetting` へ保存します。
- 保存位置が表示領域外の場合は、一部が外なら表示領域内へ詰め、完全に外なら原点へ移動して表示します。
- クリップボード変換スイッチはタイトルバーが単一所有し、`AppSetting.IsClipboardConvertEnabled` に双方向で連動します。
- プリセット選択ドロップダウンは設定画面と同一仕様で、`SettingsViewModel.SelectedPresetName` に連動します（クリップボード変換スイッチの有効/無効では Disable になりません）。
- タイトルバーのアプリ名・余白領域のマウスダウンでウィンドウをドラッグ移動できます。同領域のダブルクリックは最大化ボタンと同じ動作（通常時は最大化、最大化中は復元）になります。
- NavigationView のペイン項目（ホーム / 設定）はアイコン付きで表示します。アイコン形状は `FluentIcons` が提供します。
- NavigationView のペイン配置は `Inline` 固定です（MewUI 既定の `Auto` は利用可能幅 1000 DIP 未満でペインを内容へ重ねる形へ切り替わり、狭めるとペインが見えなくなるため）。
- プロパティは公開しておらず、NavigationView がナビゲーション状態とコンテンツ領域を所有します。

### 1.3 Helpers（`EsUtil.ZenHanConverter.App.MewUI.Helpers`）

#### 1.3.1 `FluentIcons`

アプリで使用する Fluent Icons のアイコン形状を提供します。パスデータは Core の `FluentIconData`（SVG の埋め込みリソース）が単一所有し、MewUI の形状への変換のみを担います。初回アクセス時に一度だけ生成します。

- `PathGeometry ConvertRange` — ホーム項目用の「Convert Range」アイコン形状
- `PathGeometry Settings` — 設定項目用の「Settings」アイコン形状

#### 1.3.2 `HomeView : UserControl`

ホーム画面。変換前/後表示、変換前テキストへの手動入力。プリセット選択はタイトルバーが提供します。

全角/半角変換は常に有効で、ホーム画面に有効/無効の切り替えはありません。

#### 1.3.3 `SettingsView : UserControl`

設定画面。変換カテゴリ（セグメント）、文字列の置換、プリセット管理、エクスポート/インポート。
項目数が多い「記号の変換」はマルチカラム（`MultiColumnPanel`。 型は `EsUtil.ZenHanConverter.App.MewUI.Views.MultiColumnPanel`）で表示します。
変換項目行のラベルは「名称　記号」形式を名称部と記号部に分けて表示し、記号部とセグメントコントロールが縦に揃うように配置します。
セグメントの表示は設定値の変更を購読するため、プリセット読み込み・インポートで設定が変わると表示も追従します。

#### 1.3.4 `MultiColumnPanel : Panel`（`EsUtil.ZenHanConverter.App.MewUI.Views`）

子要素を縦方向の複数列へ配置するパネル。列ごとに子要素を積み、最大列高さを最小化します（計算は `EsUtil.Feature.ColumnPacking.Core` の `ColumnPacker` へ委譲）。
列幅はエンジンが列ごとに算出します（列幅 = その列の最大アイテム幅）。子要素の幅が揃っていれば各列の幅も揃います。

- `double RowSpace` — 行間スペース（DIP、既定 4）
- `double ColumnSpace` — 列間スペース（DIP、既定 32）
- `int ColumnLimit` — 使用する列数の上限（既定 1）
- `const double DefaultRowSpace` / `const double DefaultColumnSpace` / `const int DefaultColumnLimit` — 各プロパティの既定値
- 実際の列数は幅に応じて上限以下へ減ります。幅が確定できない場合は単列として扱います。

### 1.4 Services（`EsUtil.ZenHanConverter.App.MewUI.Services`）

#### 1.4.1 `ClipboardService : IClipboardService`

クリップボードの読み書きと変更監視。Win32 の `GetClipboardSequenceNumber` ポーリングで変更を検出します。
Win32 API の呼び出しは `EsUtil.Platform.Core` の `WinClipboard`、変更検出は `EsUtil.ZenHanConverter.App.Core.Logic.ClipboardChangeDetector` へ委譲します。
ポーリングは `DispatcherTimer` で行い、`ContentChanged` を UI スレッドで発行します（購読側がバインド済みプロパティを更新するため）。

- `event EventHandler<object>? ContentChanged`

#### 1.4.2 `INavigationService`

ナビゲーションの抽象。NavigationView のコンテンツ解決（タグからビュー生成・キャッシュ）を担います。

- `Element ResolveView(string tag)` — タグに対応するビューを生成（キャッシュ）して返す

#### 1.4.3 `NavigationService : INavigationService`

NavigationView.ContentSelector からのビュー解決を実装し、HomeView / SettingsView をキャッシュします。

- `Element ResolveView(string tag)` — タグからビューを解決

## 2. エントリポイント

`Program.Main` で DI コンテナを構築し、MewUI の `ApplicationBuilder` でメインウィンドウを表示します。
