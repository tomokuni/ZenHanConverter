# ZenHanConverter.App.WinUI3 システム仕様書

本ドキュメントは `src/App.WinUI3`（WinUI 3 アプリ）のシステム仕様（概要・技術スタック・アーキテクチャ・構成・動作仕様）を記載します。
メソッド内部のコードは記載しません。

## システム概要

クリップボードのテキストを監視し、設定に基づいて全角/半角変換を実行してクリップボードへ書き戻す Windows デスクトップアプリです。
UI フレームワークとして WinUI 3（Windows App SDK）を使用します。

## 技術スタック

- .NET 11
- WinUI 3（`Microsoft.WindowsAppSDK` 2.*）
- CommunityToolkit.Mvvm 8.*（変換設定・ViewModel）
- CommunityToolkit.WinUI.Controls.*（Segmented / SettingsControls / Primitives）
- CommunityToolkit.WinUI.UI.Controls.DataGrid 7.*
- EsUtil.Ui.WinUI3.ColumnPacking（0.1.* のパッケージ参照。マルチカラム配置パネル `ColumnPackingPanel`）
- EsUtil.Ui.WinUI3.Interop（Windows App Runtime の確認と導入 `WindowsAppRuntimeBootstrap`）
- Microsoft.Extensions.Hosting 10.*（DI）

マルチカラム配置パネルは `EsUtil.Ui.WinUI3.ColumnPacking` の `ColumnPackingPanel` を使います（同梱ソースは持ちません）。
配置計算は UI 非依存の `EsUtil.Feature.ColumnPacking.Core` が単一所有し、パネルはその結果を要素の配置へ写します。

## 共有コア

`src/App.Core` は MewUI / WinUI3 に依存しない共有ライブラリです。モデル・変換ロジック・Win32 API・アイコンデータ・変換カテゴリ定義を提供し、
仕様は `../core/SPEC_ExtFunc.md` と `../core/SPEC_System.md` を参照してください。

WinUI アプリは `src/App.Core` と `src/App.Core.Presentation` のみを参照し、`src/App.MewUI`（MewUI 版）とは相互依存しません。
両アプリは同一の設定ファイル（`%LOCALAPPDATA%\ZenHanConverter`）を共有します。

```mermaid
flowchart LR
    AppMewUI[src/App.MewUI] -->|参照| Core[src/App.Core]
    AppWinUI3[src/App.WinUI3] -->|参照| Core
```

## ビルドと配置

### 通常ビルド

```powershell
dotnet build src/App.WinUI3/App.WinUI3.csproj
dotnet run --project src/App.WinUI3/App.WinUI3.csproj
```

プロジェクトは **Windows App Runtime を同梱しません**（`WindowsAppSDKSelfContained=false`）。実行環境に
導入済みの Windows App Runtime を使います。ランタイムの有無の確認と導入は
`src/App.WinUI3/Program.cs` が `EsUtil.Ui.WinUI3.Interop` の `WindowsAppRuntimeBootstrap` に任せます
（`WindowsAppSdkBootstrapInitialize=false` にして、モジュール読み込み時の自動初期化は止めています）。

`test/App.WinUI3` と `test/parity` は `WindowsAppSdkAutoInitialize=false` のため、ランタイムの無い環境でも
導入ダイアログを出さずに終了します。

### Native AOT の exe

`buildScript/publish_WinUI3.bat` が `publish\winui3-<RID>-aot\ZenHanConverter.WinUI3.exe` を出力します
（既定 RID は `win-x64`）。スクリプトはリポジトリルートを基準に動作します。

AOT 発行に必要なプロパティは次のとおりです。

1. `WindowsPackageType=None`（非パッケージ。プロジェクトで設定済み）
2. `PublishAot=true`（スクリプトが指定）と `PublishSingleFile=false`（AOT は単一のネイティブ exe を作るため、単一ファイル化との併用は失敗する）
3. `SelfContained=true`（.NET ランタイム。プロジェクトで設定済み）
4. `WindowsAppSDKSelfContained=false`（Windows App Runtime は導入済みのものを使う）
5. `EnableMsixTooling=true`（`resources.pri` の生成に必須。プロジェクトで設定済み。無いと起動時に `Microsoft.UI.Xaml.dll` で `0xc000027b` になる）
6. `AllowUnsafeBlocks=true` と `CsWinRTAotOptimizerEnabled=true`（CsWinRT が生成するコードと AOT 最適化に必要。前者はプロジェクトで設定済み、後者は Windows App SDK 2.6 が要求する）

### Windows App SDK 未使用ペイロードの除外

配布する AOT 版は Windows App Runtime を同梱しないため、AI / ML / Search / Widgets / WebView2 の
ペイロードは出力へ複製されません。「自己完結の発行（`-p:WindowsAppSDKSelfContained=true`）へ切り替えた場合」に
備えて、`App.WinUI3.csproj` の `RemoveUnusedWindowsAppSdkPayload` ターゲットが `ResolvedFileToPublish` から
未使用ファイル（`onnxruntime.dll` / `DirectML.dll` / `Microsoft.Windows.AI*` / `Microsoft.Windows.ML*` /
`Microsoft.Windows.Search*` / `Microsoft.Windows.Widgets*` / `Microsoft.Web.WebView2*` / `NPUDetect.dll` /
`PerceptiveStreaming.dll` / `msquic.dll`）を除去します。

本アプリは AI / ML / Search / Widgets / WebView2 を使用しないため、除外による機能低下はありません。

発行はリポジトリ外の一時領域（`%TEMP%\publish-ui-winui3-<RID>`）で行い、必要なファイルだけを出力先へ配置します。
リポジトリ内へ直接発行するとフレームワーク配置のファイルが出力先へ混在し、プロジェクト ディレクトリ配下の
`publish` がアプリのコンテンツ（`App.xaml` / `resources.pri` 等）として取り込まれて肥大化します。

### Native AOT の可否

Windows App SDK **2.6 では Native AOT で発行できます**（実測 2026-11-07）。成立する条件は
「Windows App Runtime を同梱しない（`WindowsAppSDKSelfContained=false`）」「`EnableMsixTooling=true` で
`resources.pri` を生成する」「`PublishSingleFile=false`」の 3 点です。旧版（Windows App SDK 1.x、自己完結の発行）
では発行は成功しても起動時に `Microsoft.UI.Xaml.dll` で `0xc000027b` になっていましたが、この構成では
起動と画面表示を確認しています。

`CommunityToolkit.WinUI.UI.Controls.DataGrid`（第三者）は AOT / トリム解析で `IL2104` を報告するため、
`App.WinUI3.csproj` の `NoWarn` で抑止しています（理由は
[`.github/ビルド設定規約.md`](../../.github/ビルド設定規約.md) の付録 A）。

置換一覧の `DataGrid` はセル テンプレートを `x:Bind`（`x:DataType` 付き）で書き、プロパティ名の実行時解決に
頼りません。設定画面の `ctk:Segmented` も、`SelectedValue` + `SelectedValuePath`（プロパティ名の実行時解決）を
使わず、`SelectedItem` を `ZenHanConvertItem.SelectedOption` へ双方向バインドします。

## src/App.WinUI3 プロジェクト

### src/App.WinUI3/Program.cs

#### `Program` static class

アプリケーションのエントリ ポイント（XAML の自動生成 Main は `DISABLE_XAML_GENERATED_MAIN` で無効）。

**メソッド**:

- `Main()` — `WindowsAppRuntimeBootstrap.TryEnsureAvailable` でランタイムを用意し、`Application.Start` で
  `App` を生成する。終了時に `WindowsAppRuntimeBootstrap.Shutdown` を呼ぶ

### src/App.WinUI3/App.xaml.cs

#### `App` partial class

アプリケーションのエントリポイント。

**静的プロパティ**:

- `Services` (`IServiceProvider`) — DI コンテナのサービスプロバイダー

**静的メソッド**:

- `GetService<T>()` → `T` — DI コンテナから指定型のサービスを取得

**メソッド**:

- `OnLaunched(LaunchActivatedEventArgs args)` — 起動時のメインウィンドウ表示と初期ページ設定

**処理フロー**:

1. コンストラクタで DI コンテナを初期化、AppSetting/ConvertConfig を自動保存モードで起動
2. OnLaunched でメインウィンドウを表示、NavigationService を初期化
3. 集約エラーハンドラー（UI/バックグラウンド/非同期タスク）を購読

---

### src/App.WinUI3/Services/INavigationService.cs

#### `INavigationService` interface

アプリケーション内のページ遷移を管理する抽象（DIP）。

`object?` / UI 要素を扱うため、共有コアではなく `src/App.WinUI3` に配置しています。

**メソッド**:

- `NavigateTo(object? page)` — 指定されたページへ遷移
- `Initialize()` — ナビゲーションサービスを初期化
- `PreloadSettingsAsync()` — SettingsPage をバックグラウンドで事前生成（UI スレッドがアイドルになったタイミング）

---

### src/App.WinUI3/Services/NavigationService.cs

#### `NavigationService` class

アプリケーション内のページ遷移を管理するサービス。

**実装**: `INavigationService`

**内部構造**:

- `_pageMap` (`FrozenDictionary<string, Type>`) — ページ名と型のマッピング（"Home" → HomePage, "Settings" → SettingsPage）
- `_pageCache` (`Dictionary<Type, UIElement>`) — キャッシュされたページインスタンス
- `_currentPageType` (`Type?`) — 現在表示中のページタイプ

**メソッド**:

- `Initialize()` — SettingsPage を事前生成（DispatcherQueue.Low 優先度）
- `NavigateTo(object? selectedPage)` — NavigationViewItem または文字列でページ遷移
- `NavigateToPage(string tag)` — 指定タグ名のページへ遷移（キャッシュあり/なしの分岐、前ページの非表示化、NavigationView の選択状態同期）

**最適化**:

- `FrozenDictionary` による高速ルックアップ
- ページインスタンスのキャッシュによる再生成コスト削減

---

### src/App.WinUI3/Services/ClipboardService.cs

#### `ClipboardService` partial class

システムクリップボードの読み書きと内容変更監視を提供するサービス。

**実装**: `IClipboardService`

**プロパティ/イベント**:

- `ContentChanged` — クリップボード内容変更イベント

**メソッド**:

- `GetTextAsync()` → `Task<string?>` — クリップボードからテキストを非同期取得
- `SetText(string text)` → `void` — クリップボードにテキストを設定
- `Flush()` → `void` — クリップボード内容を永続化
- `RaiseContentChanged()` → `void` — テスト用にイベント発行

**内部メソッド**:

- `OnClipboardContentChanged(object? sender, object e)` — システムイベントハンドラ
- `ClipboardActionSafe(Action action)` — クリップボード操作を安全に実行（アクセス拒否時の例外を無視）

**セーフガード**: バックグラウンド時の `UnauthorizedAccessException` や WinRT 相互運用の `COMException` を握り潰し

---

### src/App.WinUI3/Services/DependencyInjectionExtensions.cs

#### `DependencyInjectionExtensions` static class

DI コンテナへのサービス登録拡張メソッド。

**登録サービス（全てシングルトン）**:

- サービス: `NavigationService`, `ClipboardService`
- コアロジック: `CharConverter`
- モデル: `AppSetting`, `ConvertConfig`
- ViewModel: `MainWindowViewModel`, `HomeViewModel`, `SettingsViewModel`
- View: `MainWindow`, `HomePage`, `SettingsPage`

---

### src/App.WinUI3/ViewModels/MainWindowViewModel.cs

#### `MainWindowViewModel` partial class

メインウィンドウのデータ管理とページ遷移制御。

**依存**: `INavigationService`

**ObservableProperty**:

- `SelectedPage` (`object?`) — 現在選択されているページ（変更時に NavigateTo を自動実行）

---

### src/App.WinUI3/ViewModels/HomeViewModel.cs

#### `HomeViewModel` partial class

ホーム画面のデータ管理、クリップボード監視と文字変換実行。

**実装**: `IDisposable`

**依存**: `ConvertConfig`, `ITextConverter`, `IClipboardService`, `AppSetting`, `DispatcherQueue?`（UI スレッドのディスパッチキュー。UI 層が取得してコンストラクターから注入し、未指定の場合はバックグラウンドで処理する）

**ObservableProperty**:

- `BeforeText` (`string`) — 変換前のテキスト
- `ConvertedText` (`string`) — 変換後のテキスト

**プロパティ**:

- `Config` (`ConvertConfig`) — 変換設定
- `TestMode` (`bool`) — テスト用同期実行モード

**処理フロー**:

1. `ClipboardService.ContentChanged` イベント受信
2. `SemaphoreSlim` で排他制御
3. クリップボードからテキスト取得
4. `BeforeText` 更新（変更通知により `ConvertedText` が自動更新）
5. 変換結果が元と異なる場合のみクリップボードに書き戻し
6. `_isUpdatingClipboard` フラグで書き戻し中の再帰イベントを防止

**注意点**:

- 全角/半角変換は常に有効です（オン/オフの切り替えはありません）。変換エンジン（`CharConverter`）だけでなく、この ViewModel も
  `ConvertConfig.IsEnabledZenHan` を参照しません（MewUI 版と同一）
- プリセット選択はタイトルバー（`MainWindow`）が `SettingsViewModel` を介して単一所有し、この ViewModel は関与しません

---

### src/App.Core.Presentation/ViewModels/SettingsViewModel.cs

#### `SettingsViewModel` partial class

設定画面のデータ管理。

**依存**: `ConvertConfig`

**ObservableProperty**:

- `SelectedPresetName` (`string?`) — 現在選択されているプリセット名

**プロパティ**:

- `ConvertConfig` (`ConvertConfig`) — 変換設定への参照
- `NumberItems` / `AlphabetItems` / `KanaItems` / `SymbolItems` / `EtcZenHanAsciiItems` / `EtcBslashYenItems` / `EtcSpecialItems` / `EtcMultiSpaceItems` — 各変換カテゴリのセグメントアイテム一覧
- `PresetNames` (`ObservableCollection<string>`) — プリセット名の一覧
- `ReplaceItems` (`ObservableCollection<ReplacePairItem>`) — ユーザー定義の置換ルール一覧

**メソッド**:

- `ValidateAllAndSyncToConfig()` — 全置換行をバリデーションし有効な行のみを同期
- `ValidateItemAndSync(ReplacePairItem item)` — 指定行をバリデーションし全行同期
- `ReloadReplaceItemsFromConfig()` — ReplaceItems を ConvertConfig から再構築
- `ExportSettings(string filePath)` — JSON エクスポート
- `ImportSettings(string filePath)` → `string?` — JSON インポート
- `RefreshPresets()` — プリセット一覧を更新
- `SavePreset(string name)` — 現在の設定をプリセットとして保存
- `LoadPreset(string? name)` — プリセットを読み込み（選択名を維持、RefreshSelectedPreset による上書きは行わない）
- `DeletePreset(string? name)` — プリセットを削除

**イベント駆動**:

- `ConvertConfig.PropertyChanged` 購読 → 設定変更を検出し `RefreshSelectedPreset()` を呼び出し
- `OnSelectedPresetNameChanged` — ドロップダウン選択によるプリセット読み込み
- `_isUpdatingSelection` フラグで再帰的読み込みを防止

---

### src/App.Core.Presentation/ViewModels/ZenHanConvertItem.cs

#### `ZenHanConvertItem` partial class

設定画面の変換項目を表す ViewModel。

**実装**: `IDisposable`

**プロパティ**:

- `Label` (`string`) — 項目のラベル
- `IsEnabled` (`bool`) — コントロールの有効状態
- `ForceEnableState` (`bool?`) — 強制的な有効状態設定
- `Segments` (`SegmentItem[]`) — 選択可能なセグメント定義
- `SelectedLabel` (`string`) — 選択されている項目のラベル（getter/setter）

**最適化**:

- `ConvertConfig.Mode`（型安全なアクセサ定義）を介したプロパティアクセス
- `PropertyChanged` 購読で `SelectedLabel` を再通知

---

### src/App.Core.Presentation/ViewModels/ReplacePairItem.cs

#### `ReplacePairItem` partial class

置換ルール1行分の ViewModel。

**ObservableProperty**:

- `Search` (`string`) — 検索文字列
- `Replace` (`string`) — 置換文字列
- `IsRegex` (`bool`) — 正規表現フラグ
- `ErrorMessage` (`string?`) — バリデーションエラーメッセージ

**メソッド**:

- `Validate()` → `string?` — バリデーション実行（ReplacePair.TryValidate に委譲）
- `ToPair()` → `ReplacePair` — ViewModel からモデルへの変換

---

### src/App.WinUI3/Views/MainWindow.xaml / MainWindow.xaml.cs

#### `MainWindow` sealed partial class

アプリケーションのメインウィンドウ。

**構造**: TitleBar + NavigationView（Home/Settings） + ContentFrame（ページコンテンツ領域）

**プロパティ**:

- `ViewModel` (`MainWindowViewModel`)
- `AppSetting` (`AppSetting`) — ウィンドウサイズ・位置の保存に使用
- `ContentFrame` (`Grid`) — ページコンテンツを表示
- `NavigationView` (`NavigationView`)
- `TitleBar` (`TitleBar`)

**タイトルバーの内容**: アプリ名（`Title="ZenHanConverter"`）、ペイン開閉ボタン、
クリップボード変換スイッチ（`AppSetting.IsClipboardConvertEnabled` に双方向バインド）と、
プリセット選択の `ComboBox`。プリセットは `SettingsViewModel.SelectedPresetName` を単一所有元とし、
設定画面のドロップダウンと同じ値を双方向に同期します（`BindPresetSelector` / `SyncPresetSelection`）。
戻るボタン・副題（Subtitle）・アプリ名左のアイコン（`TitleBar.IconSource`）は使用しません。
スイッチとラベルは詰めて並べます（`Spacing` 0・スイッチの `Padding` 0）。

`TitleBar.Content` は `Grid`（`ColumnDefinitions="Auto,Auto"`・`ColumnSpacing=24`）とし、
左列にスイッチ、右列にプリセット選択を置きます。プリセット選択の幅は指定せず、
項目（プリセット名）の文字幅に合わせて自動で決めます（`MinWidth` を指定しない）。

> `TitleBar.Content` に与えられる領域はウィンドウ幅より狭く（おおよそ「ウィンドウ幅 − 436 DIP」）、
> 内容はこの領域でクリップされます。ウィンドウ幅が小さいときはプリセット名が途中で切れるため、
> 完全に表示するには 880 DIP 程度の幅が必要です。

**ナビゲーションペインの幅**: `NavigationView.OpenPaneLength` を 104 に指定します（既定の 320 より狭くし、
コンテンツ領域を広く取ります。MewUI 版のペイン幅 100 に合わせた値です）。

**処理**: アクティブ化時にウィンドウサイズと位置を AppSetting から復元、クローズ時に保存

---

### src/App.WinUI3/Views/HomePage.xaml / HomePage.xaml.cs

#### `HomePage` sealed partial class

ホーム画面を表示するページ。

**プロパティ**: `ViewModel` (`HomeViewModel`)

**UI**: 変換前/変換後のテキスト表示（全角/半角変換は常に有効のため、切り替え UI は持ちません）。
クリップボード変換スイッチとプリセット選択はタイトルバー、設定画面への遷移は `NavigationView` の設定項目が提供します。

**上下 2 分割**:

- 上下のペインは `Grid` の行の重み（`*`）で保持し、間に `Auto` 行の分割バーを置きます。
- 比率を重みで保持するため、**ウィンドウの高さが変化しても上下の比率が維持されます**。
- 分割バーは `SplitterRegion`（`Views/Controls`）が担います。WinUI の `Thumb` はシール型でカーソルを設定できず、
  既定テンプレートも持たない（ヒットテスト可能な領域が無い）ため、ポインター操作を自前で扱います。
- `SplitterRegion` は `PointerPressed` でポインターをキャプチャしてドラッグを開始し、`PointerMoved` の移動量を
  `DeltaChanged` で通知します。移動量はウィンドウ基準の座標（論理ピクセル）で算出し、
  Core の `SplitLayout.RatioFromDrag` で比率へ換算して上下限（0.1〜0.9）で補正します。
- カーソルは `ProtectedCursor`（protected）のため派生クラスから設定します。

---

### src/App.WinUI3/Views/SettingsPage.xaml / SettingsPage.xaml.cs

#### `SettingsPage` sealed partial class

設定画面を表示するページ。

**プロパティ**: `ViewModel` (`SettingsViewModel`)

**UI レイアウト（Grid.Row 3段構成）**:

1. **Grid.Row="0"（固定）**: ヘッダー（全角/半角の変換設定の見出しと説明）
2. **Grid.Row="1"（固定）**: **設定の管理**（Border 使用、ヘッダー直下に固定）
   - 左寄せ: プリセット選択 ComboBox ＋「プリセット編集」ボタン
   - 右寄せ: 「エクスポート (JSON)」＋「インポート (JSON)」ボタン
3. **Grid.Row="2"（スクロール）**: ScrollViewer 内の設定項目
   - 8つの変換カテゴリ（数字/英字/かな/記号/その他）
   - 文字列の置換（DataGrid）

**内部メソッド** (`internal static`):

- `ContainsInvalidFileNameChars(string name)` → `bool` — ファイル名に使用できない文字が含まれるかの判定
- `ContainsBuiltInKeyword(string name)` → `bool` — built-in/builtin キーワードを含むかの判定（NFKC 正規化 + 小文字変換後）

**イベントハンドラ**:

- `OnEditPresetClick` — プリセット編集ダイアログ表示（編集可能 ComboBox + 保存/削除/閉じる）
- `OnPresetComboBoxSelectionChanged` — プリセット選択ドロップダウンの変更処理
- `OnEditReplaceRowClick` — 置換行編集ダイアログ表示
- `OnExportSettingsClick` / `OnImportSettingsClick` — 設定のエクスポート/インポート（FilePicker）

---

### マルチカラム配置パネル（`EsUtil.Ui.WinUI3.ColumnPacking.ColumnPackingPanel`）

子要素を縦方向の複数列へ配置し、配置に必要なサイズを算出するパネル。`ItemsControl` の `ItemsPanel` を置き換えられます。
**同梱ソースは持たず**、EsUtil のパネルをパッケージ参照で使います（配置計算は `EsUtil.Feature.ColumnPacking.Core` が単一所有）。

**プロパティ**:

- `double RowSpace` — 行間スペース（ピクセル、既定値 8.0）
- `double ColumnSpace` — 列間スペース（ピクセル、既定値 8.0）
- `int ColumnLimit` — 最大列数（既定値 10）
- `ColumnPacker.Method Method` — 配置アルゴリズム（`DynamicProgramming` / `BinarySearch` / `Greedy`、既定値 `BinarySearch`）
- `object? ItemsSource` / `DataTemplate? ItemTemplate` — `ItemsControl` 互換の項目指定
- 直近の配置結果（読み取り専用、未計算時は null）

**使用箇所**:

- `SettingsPage` の変換カテゴリ表示（ページリソースの既定スタイルで `ItemTemplate` / 行間 / 列間 / 列数上限を設定）

---

### src/App.WinUI3/Helpers/FluentIcons.cs

#### `FluentIcons` class

Core の SVG パスデータ（`FluentIconData`）から WinUI のアイコン形状（`Geometry`）を生成します。

**プロパティ**:

- `Geometry ConvertRange` — ホーム項目用の「Convert Range」アイコン形状（初回アクセスのみ生成）
- `Geometry Settings` — 設定項目用の「Settings」アイコン形状（初回アクセスのみ生成）

**注意点**:

- WinUI の `Geometry` には静的 `Parse` がないため、XAML の型変換（`XamlBindingHelper.ConvertValue`）でパスデータを形状化します
- MewUI 版と同じ SVG を共有するため、両 UI のアイコンが一致します

---

### src/App.WinUI3/Helpers/EnableStyleSelector.cs

#### `EnableStyleSelector` class

SegmentedItem の有効/無効状態に応じたスタイルセレクター。

---

### 共有コアに移動した要素

次の要素は `src/App.WinUI3` ではなく `src/App.Core` に配置されています（MewUI 版と共有するため）。詳細は `../core/SPEC_System.md` を参照してください。

- `SegmentDefinitions` — 変換カテゴリのセグメント定義（`Core.Models`）
- `INavigationService` を除くコア抽象（`Core.Interfaces` の `IClipboardService` / `ITextConverter`）
- 変換エンジン（`Core.Logic.CharConverter`）とモデル（`Core.Models`）

---

### src/App.WinUI3/Converters/

#### `BoolToCheckMarkConverter` sealed partial class

bool 値をチェックマーク文字列に変換（IValueConverter）。

- `Convert`: true → "✅", false/null → "□"
- `ConvertBack`: 未サポート（NotSupportedException）

#### `StringNotEmptyToVisibilityConverter` sealed partial class

空文字列でない場合に Visible にするコンバーター（IValueConverter）。

- `Convert`: null/空文字 → Collapsed, それ以外 → Visible
- `ConvertBack`: 未サポート（NotSupportedException）

---

## ウィンドウ位置・サイズの復元と保存

起動時（`Activated`）は `AppSetting.WindowWidth` / `WindowHeight`（DIP）を初期サイズとして復元し、`WindowX` / `WindowY`（DIP）があれば位置も復元します。
終了時（`Closed`）は現在のサイズと位置を DIP へ換算して `AppSetting` へ書き戻し、同期で JSON 保存します（デバウンス待ちではプロセス終了に間に合わないため）。

保存位置が表示領域外の場合は Core の `ScreenVisibleArea.Clamp` で補正します。

- 一部が表示領域外: はみ出した辺を表示領域の内側へ詰めて表示
- 完全に表示領域外: 原点（プライマリモニタの左上）へ移動

表示領域は仮想画面（全モニタの外接矩形）を `EsUtil.Platform.Core` の `WinVirtualScreen` が物理ピクセルで取得し、ウィンドウの DPI スケールで DIP へ換算したものです。
WinUI の `AppWindow.Position` / `Size` は物理ピクセルを扱うため、保存・復元の両方向で `GetDpiForWindow` による換算を行います。

## クリップボード変換

`AppSetting.IsClipboardConvertEnabled` が false の場合、`HomeViewModel` はクリップボードの読み取り・書き戻しを一切行いません。
全角/半角変換は常に有効で、ホーム画面の「変更前」テキストボックスへの手動入力に対しても適用されます（クリップボード変換の有効/無効に依存しません）。

## 変換前テキストの編集

ホーム画面の「変更前」テキストボックスは編集可能です。変更すると `HomeViewModel.BeforeText` のセッター経由で自動変換され、「変更後」に反映されます（クリップボードへの書き戻しは行いません）。  
`LineNumberTextBox.IsReadOnly` の依存関係プロパティの既定値は `true` のため、編集可能にするには `IsReadOnly="False"` を明示する必要があります。

## テストプロジェクト

### フォルダ構成

テストは UI ごとのプロジェクト（`test/App.WinUI3`）に置き、共有部分（`TestHelper.cs`）は `test/TestHelper.cs` が持ちます。

| テスト | 対象 |
| --- | --- |
| `test/App.WinUI3/Converters/BoolToCheckMarkConverterTests.cs` | チェック マークへの変換 |
| `test/App.WinUI3/Converters/StringNotEmptyToVisibilityConverterTests.cs` | 空文字の可視性への変換 |
| `test/App.WinUI3/ViewModels/HomeViewModelTests.cs` | ホーム画面の表示用変換・クリップボード連携 |
| `test/App.WinUI3/ViewModels/MainWindowViewModelTests.cs` | ページ遷移 |
| `test/App.WinUI3/Views/SettingsPagePresetValidationTests.cs` | 設定画面のプリセット検証 |

- パネルは `EsUtil.Ui.WinUI3.ColumnPacking` を使うため、同梱のテストは持ちません。
- 共有コアのテストは `test/App.Core`（内容は `src/App.Core/SPEC_System.md` を参照）、共有プレゼンテーション層のテストは `test/App.Core.Presentation` にあります。

---

## 動作仕様

### クリップボード監視

1. アプリ起動後、システムクリップボードの変更イベントを購読
2. 変更を検出すると `SemaphoreSlim` で排他制御
3. クリップボードからテキストを取得し、設定に基づいて変換
4. 変換結果が元のテキストと異なる場合のみクリップボードに書き戻し
5. 書き戻し中の再帰イベントは `_isUpdatingClipboard` フラグで防止

### ページ遷移

1. `NavigationView.SelectedItem` の変更 → MainWindowViewModel.SelectedPage → NavigationService.NavigateTo
2. 設定画面への遷移は `NavigationView` の設定項目（歯車アイコン）から行う
3. ページインスタンスは DI コンテナ経由で生成され、キャッシュされる
4. 前ページは Visibility.Collapsed で非表示化

### 設定の自動保存

1. `ConvertConfig` または `AppSetting` のプロパティ変更を検出
2. 300ms のデバウンス後、JSON ファイルに非同期保存
3. デバウンス中にさらに変更があった場合はタイマーがリセット

### プリセットのマッチング

1. `GetPresetNames()` と同じ順序（組込み→保存、各グループ内で名前昇順）で先頭から一致確認
2. 最初に見つかった一致するプリセット名を `SelectedPresetName` に設定
3. 設定内容の変更は `ConvertConfig.PropertyChanged` イベントで検出
4. `LoadPreset` 実行時は `_isUpdatingSelection` フラグで再帰を防止

### プリセット編集ダイアログのバリデーション

| 条件 | メッセージ | 保存ボタン | 削除ボタン |
| --- | --- | --- | --- |
| 空文字 | なし | 無効 | 無効 |
| 組込みプリセット | 組込みプリセットです。 | 無効 | 無効 |
| ファイル名に使用不可文字を含む | 使用できない文字が含まれます。 | 無効 | 既存なら有効 |
| 半角小文字で built-in/builtin を含む | built-in または builtin は使用できません。 | 無効 | 既存なら有効 |
| 既存ユーザープリセット | 既に存在します。 | 有効 | 有効 |
| 新規有効な名前 | なし | 有効 | 無効 |

バリデーションは選択変更時、フォーカス喪失時、およびテキスト内容変化時にリアルタイム実行。

---

## パフォーマンス向上施策

- **変換ペアのキャッシュ**: `CharConverter._cachedPairs` により Config 変更時のみ再計算
- **メタデータ駆動の DRY 設計**: `ConvertConfig.Mode.All` 配列からコピー/比較/全角設定のアクション配列を動生成
- **FrozenDictionary**: ページマップと組み込みプリセットの高速ルックアップ
- **デバウンス保存**: 300ms のデバウンスで不要なファイル書き込みを抑制
- **型安全なモードアクセサ**: `ConvertConfig.Mode`（`ModePropDef`）でプロパティ名文字列・リフレクションを排除
- **GeneratedRegex**: 連続スペース検出の正規表現をコンパイル時生成
- **SettingsPage の事前生成**: NavigationService.Initialize で DispatcherQueue.Low 優先度で非同期にページインスタンスを事前生成

## 依存関係

本プロジェクトが直接参照するプロジェクトとパッケージです（版は `Directory.Packages.props` が単一所有します）。

| 種別 | 参照先 | 用途 |
| --- | --- | --- |
| プロジェクト | `ZenHanConverter.App.Core` | 変換ロジック・設定モデル・Win32 連携 |
| プロジェクト | `ZenHanConverter.App.Core.Presentation` | UI 非依存の ViewModel |
| パッケージ | `EsUtil.Feature.ZenHan.Core` | 変換ペア・記号表（変換の定義） |
| パッケージ | `EsUtil.Platform.Core` | 画面座標・ウィンドウ配置の共通実装 |
| パッケージ | `CommunityToolkit.Mvvm` | 変更通知とコマンド |
| パッケージ | `Microsoft.Extensions.Hosting` | DI コンテナ |
| パッケージ | `EsUtil.Ui.<UI>.*` | マーキー・ルーペ・列パッキングのコントロール（UI ごと） |

- 依存の版は `Directory.Packages.props` が単一所有し、復元は `packages.lock.json` で固定します（`.github/CONTRIBUTING.md`）。

## アーキテクチャ

WinUI 3 版は、UI 非依存の 2 層（`ZenHanConverter.App.Core` / `ZenHanConverter.App.Core.Presentation`）の上に、WinUI 3 のページとコードビハインドを重ねます。
DI（`Microsoft.Extensions.Hosting`）が ViewModel・サービス・ページを解決し、ページは ViewModel だけを参照します。

```mermaid
flowchart TD
    V["Views（MainWindow / HomePage / SettingsPage）"] --> VM["ViewModels（Presentation）"]
    VM --> C["Core（変換ロジック・設定モデル）"]
    S["Services（Clipboard / Navigation / UiDispatcher）"] --> VM
    E["EsUtil.Ui.WinUI3.*（パッケージ）"] --> V
```

## コンポーネント

| コンポーネント | 役割 |
| --- | --- |
| `MainWindow` | タイトルバー・ナビゲーション・ページのホスト |
| `HomePage` / `HomeViewModel` | 変換前後のテキスト編集とクリップボード連携 |
| `SettingsPage` / `SettingsViewModel` | 変換モード・置換ルール・プリセットの設定 |
| `ClipboardService` | クリップボードの監視と書き戻し |
| `NavigationService` | ページ遷移（ページ型の解決は `s_pageMap` が単一所有） |
| `DispatcherQueueUiDispatcher` | UI スレッドへの委譲 |

## 例外処理ポリシー

- 集約ハンドラー（`App.UnhandledException`・`AppDomain.UnhandledException`・`TaskScheduler.UnobservedTaskException`）で未処理例外を受け止め、ログへ記録してダイアログで通知します。
- 想定内の失敗（クリップボードが使用中・権限が無い）は捕捉し、理由をコメントで残して処理を継続します。
- 捕捉した例外はログへ記録します（`.github/CONTRIBUTING.md` の「例外の扱い」）。
