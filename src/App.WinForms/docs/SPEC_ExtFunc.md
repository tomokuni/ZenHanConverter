# App.WinForms 外部機能仕様書

本ドキュメントは `src/App.WinForms`（WinForms アプリ）の公開 API 仕様（外部機能仕様）を記載します。
メソッド内部のコードは記載しません。外部利用者に関係する情報のみを扱います。

## 目次 <!-- omit from toc -->

- [1. 共有コア](#1-共有コア)
- [2. 公開型一覧](#2-公開型一覧)
  - [2.1 ViewModels](#21-viewmodels)
    - [2.1.1 `MainWindowViewModel : ObservableObject`](#211-mainwindowviewmodel--observableobject)
    - [2.1.2 `NavigationItem`](#212-navigationitem)
    - [2.1.3 `NavigationIcon`](#213-navigationicon)
    - [2.1.4 `HomeViewModel : IDisposable`](#214-homeviewmodel--idisposable)
  - [2.2 Services（`EsUtil.ZenHanConverter.App.WinForms.Services`）](#22-servicesesutilzenhanconverterappwinformsservices)
    - [2.2.1 `INavigationService`](#221-inavigationservice)
    - [2.2.2 `ClipboardService : IClipboardService`](#222-clipboardservice--iclipboardservice)
    - [2.2.3 `DependencyInjectionExtensions`](#223-dependencyinjectionextensions)
  - [2.3 Helpers（`EsUtil.ZenHanConverter.App.WinForms.Helpers`）](#23-helpersesutilzenhanconverterappwinformshelpers)
    - [2.3.1 `FluentIcons`](#231-fluenticons)
    - [2.3.2 `SvgPathParser`（internal）](#232-svgpathparserinternal)
  - [2.4 Views（`EsUtil.ZenHanConverter.App.WinForms.Views`）](#24-viewsesutilzenhanconverterappwinformsviews)

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
- `NavigationItem` / `NavigationIcon` — ナビゲーションペインの項目（アイコンは `System.Drawing` に依存しないよう種別のみを保持）

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
- `NavigationIcon Icon` — 項目アイコンの種別（`ConvertRange` / `Settings`）

#### 2.1.3 `NavigationIcon`

ナビゲーション項目のアイコン種別を表す列挙型。パスデータへの対応は View 側（`Helpers.FluentIcons`）が担うため、
この ViewModel 層は `System.Drawing` に依存しません。

#### 2.1.4 `HomeViewModel : IDisposable`

ホーム画面のデータ管理、クリップボード監視と文字変換実行。

- `string BeforeText` — 変換前テキスト（クリップボード取り込みまたは手動入力。変更時に自動変換）
- `string ConvertedText` — 変換後テキスト
- `bool TestMode` — テスト用の同期実行モード

全角/半角変換は常に有効（オン/オフの切り替えはありません）。クリップボードの読み書き可否は `AppSetting.IsClipboardConvertEnabled` に従います。  
クリップボード変換スイッチとプリセット選択はタイトルバー（`MainForm`）が単一所有し、この ViewModel は関与しません。  
UI スレッド以外から `IClipboardService.ContentChanged` が発行された場合、生成時に捕捉した `SynchronizationContext` へ処理を委譲します。

### 2.2 Services（`EsUtil.ZenHanConverter.App.WinForms.Services`）

#### 2.2.1 `INavigationService`

`Control` を扱うため、共有コアではなくアプリ層に配置しています。

- `Control ResolveView(string tag)` — タグに対応するビューの解決（同一タグは同一インスタンス）

#### 2.2.2 `ClipboardService : IClipboardService`

- `ClipboardService()` — 生成と同時にクリップボードの変更監視を開始
- `void RaiseContentChanged()` — テスト用にイベントを発行

#### 2.2.3 `DependencyInjectionExtensions`

- `IServiceCollection AddZenHanConverterServices(this IServiceCollection services)` — 全サービスの登録

### 2.3 Helpers（`EsUtil.ZenHanConverter.App.WinForms.Helpers`）

#### 2.3.1 `FluentIcons`

- `Image ConvertRange` / `Image Settings` — アイコン画像（共有インスタンス。呼び出し側で破棄しないこと）
- `Image Get(NavigationIcon icon)` — 種別に対応するアイコン画像の取得

#### 2.3.2 `SvgPathParser`（internal）

- `static GraphicsPath Parse(string pathData)` — SVG パスデータを GDI+ の図形パスへ変換

M / L / H / V / C / S / Q / T / Z と各相対コマンドに対応し、円弧（A / a）には対応しません。

| 例外 | 発生条件 |
| --- | --- |
| `ArgumentException` | パスデータが null または空 |
| `FormatException` | 数値またはコマンドの並びが不正 |
| `NotSupportedException` | 円弧（A / a）コマンドが含まれる |

### 2.4 Views（`EsUtil.ZenHanConverter.App.WinForms.Views`）

`System.Windows.Forms.Form` / `UserControl` を継承する画面クラスです。UI を組み立てるのみで、ロジックは持ちません。

- `MainForm : Form` — メインウィンドウ（`MainWindowViewModel ViewModel` / `AppSetting AppSetting` を公開）
- `HomeView : UserControl` — ホーム画面（`HomeViewModel ViewModel` を公開）
- `SettingsView : UserControl` — 設定画面（`SettingsViewModel ViewModel` を公開）
- `PresetEditDialog : Form` — プリセット編集ダイアログ（`PresetEditDialogViewModel ViewModel` を公開）

`AppTheme`、`CategorySection`、`ReplaceSection`、`SectionStackPanel`、`SegmentButton`、`ToggleSwitch`、`PresetComboBox`、`MessageDialog` は
本アプリ内部でのみ使用するため公開しません。
