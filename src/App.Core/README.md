# ZenHanConverter.App.Core

クリップボードの全角/半角自動変換アプリのコアロジック・モデル・プラットフォーム API ライブラリです。
**MewUI にも WinUI3 にも依存しない**ため、両方の UI 実装から共有できます。
`src/App.MewUI`（MewUI アプリ）から参照されます。

## 1. 概要

`ZenHanConverter.App.Core` は、全角/半角・ひらがな/カタカナ・記号の変換ロジック、設定モデル、Win32 連携を提供する UI 非依存のライブラリです。
4 つの UI（MewUI / WinUI 3 / Avalonia UI / WinForms）が同じ変換結果と設定ファイルを共有します。

## 2. 使い方

```csharp
var config = new ConvertConfig();
config.Initialize();                        // %LOCALAPPDATA%\ZenHanConverter\Settings.json を読む
var converter = new CharConverter(config);
var converted = converter.Convert("ＡＢＣ１２３");
```

- 設定の保存先は `%LOCALAPPDATA%\ZenHanConverter`（`AppPaths` が単一所有）です。
- 変換の定義（変換ペア・記号表）は EsUtil のパッケージ（`EsUtil.Feature.ZenHan.Core`）が単一所有します。

## 3. 構成

- `Enums/` — 変換モード列挙型（`ZenHanMode` ほか）
- `Models/` — `ConvertConfig`, `ReplacePair`, `SegmentItem`, `SegmentDefinitions`, `AppSetting`, `SettingsPersistenceBase`
- `Interfaces/` — `IClipboardService`, `ITextConverter`
- `Logic/` — `CharConverter`（変換ロジック）, `ClipboardChangeDetector`（クリップボード変更検出）
- `Helpers/` — `ZenHanConverterExtension`（変換ペア解決拡張）, `WindowPlacement`（ウィンドウ位置補正）, `SplitLayout`（上下 2 分割の比率）
- `Native/` — 仮想画面と DIP ⇔ 物理ピクセルの換算（`ScreenVisibleArea`）
- `Icons/` — Fluent Icons の SVG アセットとパスデータ提供（`FluentIconData`）

## 4. 依存

- `CommunityToolkit.Mvvm` — ObservableProperty 生成・変更通知
- `EsUtil.Feature.ZenHan.Core`（パッケージ参照。 版は `Directory.Build.props` の `EsUtilVersion`）— 全角/半角変換ペア
- `EsUtil.Platform.Core`（パッケージ参照。 版は `EsUtilVersion`）— 幾何型（`EsPoint` / `EsSize` / `EsRect`）・DPI 換算・クリップボードと仮想画面の Win32 実装

UI フレームワークには依存しません。 Win32 API の呼び出しは `EsUtil.Platform.Core` の `Native.Win` が単一所有します。

## 5. 主なクラス

- `ConvertConfig` — 変換設定（自動永続化・プリセット管理）
- `SegmentDefinitions` — 設定画面の変換カテゴリ定義（ラベル・セグメント項目・バインド先）
- `CharConverter` — テキスト変換の実行
- `ClipboardChangeDetector` — シーケンス番号によるクリップボード内容変更の検出
- `WindowPlacement` — 保存位置の表示領域内への補正（DIP の幾何型は `EsUtil.Platform.Core`）
- `ScreenVisibleArea` — 仮想画面の取得（`EsUtil.Platform.Core` の `WinVirtualScreen`）と DIP ⇔ 物理ピクセルの換算
- `FluentIconData` — Fluent Icons の SVG パスデータ（MewUI / WinUI3 の双方で利用可能）

## 6. テスト

テストは `test/App.Core` にあります。 リポジトリ ルートで次を実行します（合否基準は [.github/CONTRIBUTING.md](../../.github/CONTRIBUTING.md)）。

`powershell
dotnet test --solution ZenHanConverter.slnx -c Release
`

## 7. ドキュメント

| ドキュメント | 内容 |
| --- | --- |
| [README.md](README.md) | 本ドキュメント（機能・画面・使い方） |
| [SPEC_ExtFunc.md](docs/SPEC_ExtFunc.md) | 外部機能仕様書（公開 API） |
| [SPEC_System.md](docs/SPEC_System.md) | システム仕様書（構成・動作仕様） |
| [.github/CONTRIBUTING.md](../../.github/CONTRIBUTING.md) | 開発者向け（ビルド・テスト・CI） |
| [.github/RELEASE.md](../../.github/RELEASE.md) | リリース手順 |

## 8. ライセンス

[MIT License](../../LICENSE)
