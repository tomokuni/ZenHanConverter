# App.WinUI3 外部機能仕様書

本ドキュメントは `src/App.WinUI3`（WinUI 3 アプリ）の公開 API 仕様（外部機能仕様）を記載します。
メソッド内部のコードは記載しません。外部利用者に関係する情報のみを扱います。

## 目次 <!-- omit from toc -->

- [1. 共有コア](#1-共有コア)
- [2. 公開型一覧](#2-公開型一覧)
  - [2.1 ViewModels](#21-viewmodels)
    - [2.1.1 `HomeViewModel : IDisposable`](#211-homeviewmodel--idisposable)
    - [2.1.2 `MainWindowViewModel`](#212-mainwindowviewmodel)
  - [2.2 Services（`EsUtil.ZenHanConverter.App.WinUI3.Services`）](#22-servicesesutilzenhanconverterappwinui3services)
    - [2.2.1 `INavigationService`](#221-inavigationservice)
    - [2.2.2 `NavigationService : INavigationService`](#222-navigationservice--inavigationservice)
    - [2.2.3 `ClipboardService : IClipboardService`](#223-clipboardservice--iclipboardservice)
    - [2.2.4 `DependencyInjectionExtensions`](#224-dependencyinjectionextensions)
  - [2.3 Helpers（`EsUtil.ZenHanConverter.App.WinUI3.Helpers`）](#23-helpersesutilzenhanconverterappwinui3helpers)
    - [2.3.1 `FluentIcons`](#231-fluenticons)
    - [2.3.2 `EnableStyleSelector`](#232-enablestyleselector)
  - [2.4 Converters（`EsUtil.ZenHanConverter.App.WinUI3.Converters`）](#24-convertersesutilzenhanconverterappwinui3converters)
    - [2.4.1 `BoolToCheckMarkConverter`](#241-booltocheckmarkconverter)
    - [2.4.2 `StringNotEmptyToVisibilityConverter`](#242-stringnotemptytovisibilityconverter)
  - [2.5 Views（`EsUtil.ZenHanConverter.App.WinUI3.Views`）](#25-viewsesutilzenhanconverterappwinui3views)

## 1. 共有コア

モデル・変換ロジック・Win32 API・アイコンデータ・変換カテゴリ定義は `src/App.Core`（MewUI / WinUI3 非依存）が提供します。
公開 API は `../../App.Core/docs/SPEC_ExtFunc.md` を参照してください。

## 2. 公開型一覧

### 2.1 ViewModels

設定画面の ViewModel は **共有プレゼンテーション層（`src/App.Core.Presentation`）** が提供します。
公開 API は `../../App.Core.Presentation/docs/SPEC_ExtFunc.md` を参照してください。

本アプリ固有の ViewModel は次のとおりです。

- `MainWindowViewModel` — ナビゲーションと画面解決、タイトルバーが参照する設定の公開
- `HomeViewModel` — ホーム画面（クリップボード監視と文字変換）

#### 2.1.1 `HomeViewModel : IDisposable`

ホーム画面のデータ管理、クリップボード監視と文字変換実行。

- `string BeforeText` — 変換前テキスト（クリップボード取り込みまたは手動入力。変更時に自動変換）
- `string ConvertedText` — 変換後テキスト
- `bool TestMode` — テスト用の同期実行モード

全角/半角変換は常に有効（オン/オフの切り替えはありません）。クリップボードの読み書き可否は `AppSetting.IsClipboardConvertEnabled` に従います。  
クリップボード変換スイッチとプリセット選択はタイトルバー（`MainWindow`）が単一所有し、この ViewModel は関与しません。  
UI スレッドのディスパッチキューは UI 層（`DependencyInjectionExtensions`）が取得してコンストラクターから渡します（ViewModel は静的に取得しません）。

#### 2.1.2 `MainWindowViewModel`

メインウィンドウのページ遷移制御。

- `object? SelectedPage` — 現在選択されているページ（変更時に `INavigationService.NavigateTo` を実行）

### 2.2 Services（`EsUtil.ZenHanConverter.App.WinUI3.Services`）

#### 2.2.1 `INavigationService`

ナビゲーションの抽象。`object?` を扱うため WinUI アプリ側に配置しています。

- `void NavigateTo(object? page)` — 指定されたページへ遷移
- `void Initialize()` — ナビゲーションサービスの初期化
- `void PreloadSettingsAsync()` — SettingsPage をバックグラウンドで事前生成

#### 2.2.2 `NavigationService : INavigationService`

`FrozenDictionary` によるページ名→型のマップと、ページインスタンスのキャッシュを提供します。

#### 2.2.3 `ClipboardService : IClipboardService`

WinRT の `Clipboard` API によるクリップボードの読み書きと、`Clipboard.ContentChanged` による変更監視。

- `event EventHandler<object>? ContentChanged`

#### 2.2.4 `DependencyInjectionExtensions`

- `IServiceCollection AddZenHanConverterServices(this IServiceCollection services)` — アプリの全サービスを登録

### 2.3 Helpers（`EsUtil.ZenHanConverter.App.WinUI3.Helpers`）

#### 2.3.1 `FluentIcons`

アプリで使用する Fluent Icons のアイコン形状を提供します。パスデータは Core の `FluentIconData`（SVG の埋め込みリソース）が単一所有し、WinUI の形状への変換のみを担います。初回アクセス時に一度だけ生成します。

- `Geometry ConvertRange` — ホーム項目用の「Convert Range」アイコン形状
- `Geometry Settings` — 設定項目用の「Settings」アイコン形状

MewUI 版（`EsUtil.ZenHanConverter.App.MewUI.Helpers.FluentIcons`）と同じ SVG を共有するため、両 UI のアイコンが一致します。  
WinUI の `Geometry` には静的 `Parse` がないため、XAML の型変換（`XamlBindingHelper.ConvertValue`）でパスデータを形状化します。

#### 2.3.2 `EnableStyleSelector`

`SegmentedItem` の有効/無効状態に応じたスタイルセレクター。

### 2.4 Converters（`EsUtil.ZenHanConverter.App.WinUI3.Converters`）

#### 2.4.1 `BoolToCheckMarkConverter`

bool をチェックマーク文字列へ変換します（true → "✅" / false・null → "□"）。

#### 2.4.2 `StringNotEmptyToVisibilityConverter`

null・空文字を `Collapsed`、それ以外を `Visible` へ変換します。

### 2.5 Views（`EsUtil.ZenHanConverter.App.WinUI3.Views`）

- `App : Application` — エントリポイント（DI 構築・集約エラーハンドラー・メインウィンドウ表示）
- `MainWindow` — タイトルバー + `NavigationView` によるナビゲーションシル。
  タイトルバーにはクリップボード変換スイッチ（`AppSetting.IsClipboardConvertEnabled`）とプリセット選択の `ComboBox`
  （`SettingsViewModel.SelectedPresetName` を単一所有元として設定画面と同期）を配置します。  
  ナビゲーション項目のアイコンは `FluentIcons`（Core の SVG パスデータ）を `PathIcon` で描画します（MewUI 版と同一形状）
- `HomePage` — ホーム画面（全角半角変換スイッチ、変更前/後表示）。
  クリップボード変換スイッチとプリセット選択はタイトルバー、設定画面への遷移は `NavigationView` の設定項目が提供します
- `SettingsPage` — 設定画面（セグメント・置換ルール・プリセット管理・入出力）

> 補足: WinUI の `TitleBar` コントロール（Windows App SDK 1.7+）は `Content` に置いた操作系コントロール（`ToggleSwitch` / `ComboBox`）へ入力が届きます（実機検証済み）。
> `Window.SetTitleBar` を自前の要素に対して使う場合は `InputNonClientPointerSource.SetRegionRects` によるパススルー指定が必要ですが、
> `TitleBar` コントロールはこれを内部で処理します。
