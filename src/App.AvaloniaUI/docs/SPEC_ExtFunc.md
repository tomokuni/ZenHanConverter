# App.AvaloniaUI 外部機能仕様書

本ドキュメントは `src/App.AvaloniaUI`（Avalonia UI アプリ）の公開 API 仕様（外部機能仕様）を記載します。
メソッド内部のコードは記載しません。外部利用者に関係する情報のみを扱います。

## 目次 <!-- omit from toc -->

- [1. 共有コア](#1-共有コア)
- [2. 公開型一覧](#2-公開型一覧)
  - [2.1 ViewModels](#21-viewmodels)
    - [2.1.1 `MainWindowViewModel : ObservableObject`](#211-mainwindowviewmodel--observableobject)
    - [2.1.2 `NavigationItem`](#212-navigationitem)
    - [2.1.3 `HomeViewModel : IDisposable`](#213-homeviewmodel--idisposable)
  - [2.2 Views（`EsUtil.ZenHanConverter.App.AvaloniaUI.Views`）](#22-viewsesutilzenhanconverterappavaloniauiviews)
    - [2.2.1 `MainWindow : Window`](#221-mainwindow--window)
    - [2.2.2 `HomeView : UserControl`](#222-homeview--usercontrol)
    - [2.2.3 `SettingsView : UserControl`](#223-settingsview--usercontrol)
    - [2.2.4 `PresetEditDialog : Window`](#224-preseteditdialog--window)
    - [2.2.5 `MessageDialog : Window`](#225-messagedialog--window)
    - [2.2.6 `CategorySection : UserControl`（`Views.Controls`）](#226-categorysection--usercontrolviewscontrols)
  - [2.3 Services（`EsUtil.ZenHanConverter.App.AvaloniaUI.Services`）](#23-servicesesutilzenhanconverterappavaloniauiservices)
    - [2.3.1 `INavigationService` interface](#231-inavigationservice-interface)
    - [2.3.2 `NavigationService : INavigationService`](#232-navigationservice--inavigationservice)
    - [2.3.3 `ClipboardService : IClipboardService`](#233-clipboardservice--iclipboardservice)
  - [2.4 Helpers（`EsUtil.ZenHanConverter.App.AvaloniaUI.Helpers`）](#24-helpersesutilzenhanconverterappavaloniauihelpers)
    - [2.4.1 `FluentIcons` static class](#241-fluenticons-static-class)
  - [2.5 Converters（`EsUtil.ZenHanConverter.App.AvaloniaUI.Converters`）](#25-convertersesutilzenhanconverterappavaloniauiconverters)
    - [2.5.1 `StringNotEmptyToBoolConverter`](#251-stringnotemptytoboolconverter)

## 1. 共有コア

モデル・変換ロジック・Win32 API・アイコンデータ・変換カテゴリ定義は `src/App.Core`（UI 非依存）が提供します。
公開 API は `../../App.Core/docs/SPEC_ExtFunc.md` を参照してください。

## 2. 公開型一覧

### 2.1 ViewModels

設定画面の ViewModel は **共有プレゼンテーション層（`src/App.Core.Presentation`）** が提供します。
公開 API は `../../App.Core.Presentation/docs/SPEC_ExtFunc.md` を参照してください。

本アプリ固有の ViewModel は次のとおりです。

- `MainWindowViewModel` — ナビゲーションと画面解決、タイトルバーが参照する設定の公開
- `HomeViewModel` — ホーム画面（クリップボード監視と文字変換）
- `NavigationItem` — ナビゲーションペインの項目（アイコン形状が Avalonia の `Geometry` のため本アプリに残す）

#### 2.1.1 `MainWindowViewModel : ObservableObject`

メインウィンドウのデータ管理（ナビゲーションと画面解決、タイトルバーが参照する設定の公開）。

- `AppSetting AppSetting` — アプリ設定（タイトルバーのクリップボード変換スイッチが参照）
- `SettingsViewModel Settings` — 設定画面の ViewModel（タイトルバーのプリセット選択が参照）
- `IReadOnlyList<NavigationItem> NavItems` — ナビゲーションペインの項目一覧
- `NavigationItem? SelectedNavItem` — 選択中のナビゲーション項目
- `Control? CurrentView` — 現在表示中のビュー（選択項目の変更で解決される）

#### 2.1.2 `NavigationItem`

ナビゲーションペインの項目。

- `string Tag` — ページタグ（"Home" / "Settings"）
- `string Label` — 表示ラベル
- `Geometry Icon` — 項目アイコンの形状

#### 2.1.3 `HomeViewModel : IDisposable`

ホーム画面のデータ管理、クリップボード監視と文字変換実行。

- `string BeforeText` — 変換前テキスト（クリップボード取り込みまたは手動入力。変更時に自動変換）
- `string ConvertedText` — 変換後テキスト
- `bool TestMode` — テスト用の同期実行モード

全角/半角変換は常に有効（オン/オフの切り替えはありません）。クリップボードの読み書き可否は `AppSetting.IsClipboardConvertEnabled` に従います。  
クリップボード変換スイッチとプリセット選択はタイトルバー（`MainWindow`）が単一所有し、この ViewModel は関与しません。

### 2.2 Views（`EsUtil.ZenHanConverter.App.AvaloniaUI.Views`）

#### 2.2.1 `MainWindow : Window`

メインウィンドウ。カスタムタイトルバー（アプリ名・クリップボード変換スイッチ・プリセット選択・ウィンドウ操作ボタン）、ナビゲーションペイン、ウィンドウ位置・サイズの復元/保存。

- 装飾は `WindowDecorations="None"` としてアプリが描画し、タイトルバー領域に `WindowDecorationProperties.ElementRole="TitleBar"` を指定してドラッグ移動とダブルクリック最大化を有効にします。
- タイトルバー内の操作系コントロールには `ElementRole="User"` を指定し、ドラッグ領域による入力の横取りから除外します。
- ウィンドウ外周（各辺・各角）に `Resize*` の役割を宣言し、リサイズを可能にします。
- 起動時は `AppSetting.WindowWidth` / `WindowHeight`（DIP）をサイズとして復元し、`WindowX` / `WindowY`（DIP）を物理ピクセルへ換算して適用します（表示領域外は Core の `ScreenVisibleArea` で補正）。
- 終了時は現在のサイズと位置を DIP へ換算して `AppSetting` へ保存し、同期で JSON 保存します。
- 最大化/最小化中に終了した場合は、通常状態での最後の位置とサイズを保存します。

#### 2.2.2 `HomeView : UserControl`

ホーム画面。変換前（編集可能）/変換後のテキスト表示。

#### 2.2.3 `SettingsView : UserControl`

設定画面。カテゴリ別の変換設定（セグメント選択）、文字列の置換、プリセット選択・編集、JSON のエクスポート/インポート。

- ファイルの選択は Avalonia の StorageProvider（`TopLevel.StorageProvider`）で行い、パス解決のみを View が担います。

#### 2.2.4 `PresetEditDialog : Window`

プリセットの保存・削除ダイアログ。名前の検証結果に応じて保存/削除ボタンが有効/無効になります。

#### 2.2.5 `MessageDialog : Window`

メッセージ表示用のシンプルなダイアログ。

- `static Task ShowAsync(Control owner, string message)` — 指定コントロールを所有ウィンドウとしてメッセージを表示

#### 2.2.6 `CategorySection : UserControl`（`Views.Controls`）

変換カテゴリ 1 セクション（見出し・説明・変換項目一覧・補足）を表示する再利用コントロール。

- `string Header` / `string Description` / `string Remark` — 見出し・説明・補足（空の場合は非表示）
- `IEnumerable? Items` — 表示する変換項目（`ZenHanConvertItem` の列挙）

### 2.3 Services（`EsUtil.ZenHanConverter.App.AvaloniaUI.Services`）

#### 2.3.1 `INavigationService` interface

タグからビューを解決する抽象（DIP）。

- `Control ResolveView(string tag)` — タグに対応するビュー（同一タグは同一インスタンス）
- `void PreloadView(string tag)` — UI スレッドのアイドル時にビューを事前生成

#### 2.3.2 `NavigationService : INavigationService`

タグとビュー型のマッピング（`FrozenDictionary`）とビューのキャッシュを保持します。

#### 2.3.3 `ClipboardService : IClipboardService`

クリップボードの読み書きと変更監視。読み書きは `EsUtil.Platform.Core` の `WinClipboard`、変更検出は Core の `ClipboardChangeDetector` へ委譲し、`DispatcherTimer` でポーリングします。

- `event EventHandler<object>? ContentChanged` — 内容変更イベント（UI スレッドで発行）
- `Task<string?> GetTextAsync()` / `void SetText(string text)` / `void Flush()`
- `void RaiseContentChanged()` — テスト用の発火

### 2.4 Helpers（`EsUtil.ZenHanConverter.App.AvaloniaUI.Helpers`）

#### 2.4.1 `FluentIcons` static class

Core のパスデータから Avalonia のアイコン形状を提供します。パスデータは Core の `FluentIconData` が単一所有し、初回アクセス時に一度だけ生成します。

- `Geometry ConvertRange` — ホーム項目用の「Convert Range」アイコン形状
- `Geometry Settings` — 設定項目用の「Settings」アイコン形状

### 2.5 Converters（`EsUtil.ZenHanConverter.App.AvaloniaUI.Converters`）

#### 2.5.1 `StringNotEmptyToBoolConverter`

文字列が空でない場合に `true` を返します（任意項目の `IsVisible` へのバインドに使用）。

- `static readonly StringNotEmptyToBoolConverter Instance` — 共有インスタンス
