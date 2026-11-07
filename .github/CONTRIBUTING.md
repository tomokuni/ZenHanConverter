# 開発者向け情報（ビルド・テスト・CI）

本ドキュメントは ZenHanConverter（リポジトリ名は `ZenHanConverter`）を**ビルド・テストする開発者**向けの情報です。
アプリの利用方法は [`../README.md`](../README.md)、各 UI の利用仕様は `../src/App.<UI>/README.md`（例: `../src/App.MewUI/README.md`）を参照してください。

- リリース手順は [`RELEASE.md`](RELEASE.md)、他のリポジトリへの流用方法は [`REUSING.md`](REUSING.md) に分離しています。

## リポジトリ構成

```text
.
├── Directory.Build.props           # 共通プロパティ（LangVersion / Nullable / ImplicitUsings / リリースバージョン）
├── global.json                     # .NET SDK のバージョンとテスト ランナー（ワークフローでは指定しない）
├── ZenHanConverter.slnx
├── .github/
│   ├── CONTRIBUTING.md            # 本ドキュメント（開発者向け）
│   ├── RELEASE.md                 # リリース手順
│   ├── REUSING.md                 # 他のリポジトリへの流用方法
│   ├── release-config.json        # リリース設定（UI 定義・ソリューション・ブランチ等の単一ソース）
│   ├── dependabot.yml             # GitHub Actions / NuGet の更新 PR
│   ├── actions/read-config/       # release-config.json を読む共通アクション
│   ├── rulesets/tag-version.json  # Tag ruleset の定義（Settings へインポートする）
│   ├── workflows/build.yml        # push / PR でビルド・テスト（配布物は作らない）
│   ├── workflows/publish.yml      # 全 UI の publish（release から呼ばれる共通ワークフロー）
│   ├── workflows/release.yml      # 手動実行で検証・publish・タグ・Release 作成
│   └── scripts/                   # version.ps1（バージョン規則） / set-version.ps1 / verify-release-version.ps1 / show-current-versions.ps1
├── buildScript/             # ビルド・実行スクリプト（リポジトリルートを基準に動作する）
│   ├── publish_ui.bat              # 発行の共通手順（UI ごとのスクリプトから呼ぶ）
│   ├── publish_MewUI.bat           # MewUI 版（Native AOT）
│   ├── publish_WinUI3.bat          # WinUI 3 版（Native AOT + ネイティブ DLL + XAML リソース）
│   ├── publish_AvaloniaUI.bat      # Avalonia UI 版（Native AOT + ネイティブ DLL）
│   ├── publish_WinForms.bat        # WinForms 版（自己完結・単一 exe）
│   ├── verify.ps1                  # 検証の入口（合否項目は実行時に出力。 .github/検証手順.md が所有者）
│   ├── verifyDocs.ps1 / VerifyCommon.ps1 / verifyPublish.ps1 / verifyInfoStyle.ps1
├── src/
│   ├── App.MewUI/           # MewUI アプリ本体（MewUI 依存）
│   │   ├── README.md / SPEC_ExtFunc.md / SPEC_System.md
│   ├── App.WinUI3/          # WinUI 3 アプリ本体（WinUI 3 依存）
│   │   ├── README.md / SPEC_ExtFunc.md / SPEC_System.md
│   ├── App.AvaloniaUI/      # Avalonia UI アプリ本体（Avalonia UI 依存）
│   │   ├── README.md / SPEC_ExtFunc.md / SPEC_System.md
│   ├── App.WinForms/        # WinForms アプリ本体（Windows Forms 依存）
│   │   ├── README.md / SPEC_ExtFunc.md / SPEC_System.md
│   ├── core/                # 共有コア（モデル・変換ロジック・カテゴリ定義・Win32 API・アイコン。UI 非依存）
│   │   ├── README.md / SPEC_ExtFunc.md / SPEC_System.md
│   └── App.Core.Presentation/   # 共有プレゼンテーション層（設定画面の ViewModel。UI 非依存）
│       ├── README.md / SPEC_ExtFunc.md / SPEC_System.md
└── test/                    # 単体テスト（xUnit v3）
    ├── App.MewUI/           # App.MewUI のテスト
    ├── App.WinUI3/          # App.WinUI3 のテスト
    ├── App.AvaloniaUI/      # App.AvaloniaUI のテスト
    ├── App.WinForms/        # App.WinForms のテスト
    ├── core/                # core のテスト（UI フレームワーク非依存）
    ├── App.Core.Presentation/   # App.Core.Presentation のテスト（UI フレームワーク非依存）
    └── parity/              # アプリ固有 ViewModel の挙動一致を検証（README.md に差異一覧）
```

## プロジェクト

| プロジェクト | 説明 |
| --- | --- |
| `src/App.Core` | 変換ロジック・モデル・変換カテゴリ定義・表示領域の補正・アイコンデータ（`CharConverter`, `ConvertConfig`, `SegmentDefinitions`, `ScreenVisibleArea`, `FluentIconData` など）。UI フレームワークに依存しない。Win32 実装と変換ペアは EsUtil が単一所有する。 |
| `src/App.Core.Presentation` | 4 つの UI が共有する設定画面の ViewModel（`SettingsViewModel`, `ZenHanConvertItem`, `SegmentOption`, `ReplacePairItem`, `PresetEditDialogViewModel`）。UI フレームワークに依存しない。 |
| `src/App.MewUI` | MewUI アプリ本体。クリップボード監視・ホーム/設定画面。 |
| `src/App.WinUI3` | WinUI 3 アプリ本体。同上。 |
| `src/App.AvaloniaUI` | Avalonia UI アプリ本体。同上。 |
| `src/App.WinForms` | Windows Forms アプリ本体。同上。 |
| `test/App.Core` | `src/App.Core` の単体テスト。ソースファイル 1 つにつき `{対象ソースファイル名}Tests.cs` を作成する。 |
| `test/App.MewUI` | `src/App.MewUI` の単体テスト。同上。 |
| `test/App.WinUI3` | `src/App.WinUI3` の単体テスト。同上。 |
| `test/App.AvaloniaUI` | `src/App.AvaloniaUI` の単体テスト。同上。 |
| `test/App.WinForms` | `src/App.WinForms` の単体テスト。同上。 |
| `test/App.Core.Presentation` | `src/App.Core.Presentation` の単体テスト。 |
| `test/parity` | アプリ固有の ViewModel（`HomeViewModel`）を同じ入力で比較し、挙動の一致を検証する。意図的な差異は `test/parity/README.md` を参照。 |

## ビルド

```powershell
dotnet build ZenHanConverter.slnx
```

### 名前空間と using

- 名前空間のルートは `EsUtil` 配下の製品名フォルダーです（例: `AppSetting` は `EsUtil.ZenHanConverter.App.Core.Models` にあります）。
- アセンブリ名・exe 名・設定ファイルの保存先（`%LOCALAPPDATA%\ZenHanConverter`）は `ZenHanConverter` のままです（**リポジトリ名 `ZenHanConverter` とは別**。 利用者の設定と配布物の互換性を保つため）。
- 暗黙の global using（`ImplicitUsings`）は無効です。各ソースファイルが使用する名前空間を `using` で明示し、プロジェクト共通の `GlobalUsings.cs` は置きません。

### 依存パッケージ

`PackageReference` には**メジャーバージョンのみ**を指定します（例: `Version="8.*"`）。
0.x のパッケージはメジャーが 0 で互換性が保証されないため、**マイナーまで**を指定します（例: `Version="0.1.*"`）。
同一範囲内の最新版が復元時に選ばれるため、常に最新の状態でビルドできます。

- EsUtil のパッケージ（`EsUtil.Feature.ZenHan.Core` / `EsUtil.Platform.Core` / `EsUtil.Feature.ColumnPacking.Core` / `EsUtil.Ui.WinUI3.ColumnPacking`）は nuget.org の公開版を参照します。
  **未公開の版を参照すると CI は復元に失敗します。** ローカルでは `Directory.Build.props` が隣の EsUtil リポジトリの
  ローカル フィード（`..\EsUtil\.nupkg`）を追加のソースにするため、公開前の版でも復元できます。

- メジャーが上がる変更（破壊的変更を含む）は自動では取り込まれません。手動で更新します。
- **浮動指定（`12.*` など）は復元時に同一メジャーの最新版を解決します。** そのため更新が必要になるのはメジャーが上がったときだけです。
  更新の有無は `dotnet list ZenHanConverter.slnx package --outdated` で確認できます（プレリリースも見る場合は `--include-prerelease`）。
- **推移依存（`SkiaSharp` など）は親パッケージが指定する範囲の最小版が選ばれます。** 直接参照で強制すると
  （例: Avalonia 12 に対する `SkiaSharp` 4 系・`HarfBuzzSharp` 14 系）描画や Native AOT が壊れる恐れがあるため、更新しません。
- テストは **xunit.v3 4 系**（Microsoft.Testing.Platform）を使用します。VSTest 専用の `Microsoft.NET.Test.Sdk` / `xunit.runner.visualstudio` / `coverlet.collector` は参照しません。
- .NET SDK のバージョンとテスト ランナーはリポジトリルートの `global.json` が単一所有します（ワークフローへ `dotnet-version` を書きません）。
  - **.NET SDK はランナー イメージに含まれる最新版をそのまま使います**（`actions/setup-dotnet` は使いません）。
    イメージの SDK は `C:\Program Files\dotnet` に入り PATH も通っているため、そのまま `dotnet` を実行できます。
    `global.json` は `rollForward: latestFeature` のため、イメージの 10.0 SDK で要件を満たします。
  - キャッシュは NuGet の復元のみを対象にします（最も時間を使う工程）。**`build.yml` は共通の 1 つ**（全 13 プロジェクトをビルドするため約 1.9GB が必要）、
    **`publish.yml` は UI ごと**（4 ジョブが同時に走るため共通キャッシュを 4 重に復元すると競合し、不要な分まで復元することになる。MewUI は約 50MB、Avalonia UI は約 1.2GB）です。
  - 参考実測（`build.yml`）: 内訳は NuGet キャッシュの復元 約 41 秒 / ビルド 45 秒 / テスト・その他 30 秒です。
    **支配的なのは NuGet キャッシュの復元**で、`Avalonia.Skia` が Linux / WebAssembly のネイティブ アセット（約 480MB）を
    無条件に要求するため、build.yml ではこれ以上減らせません（`publish.yml` は UI ごとの分割で対応済み）。
- リリースバージョン（`<Version>`）は `Directory.Build.props` が単一所有します。自動インクリメントは行わず、リリース時にワークフローが設定します（詳細は [`RELEASE.md`](RELEASE.md)）。
- 復元結果は `project.assets.json`（`obj/` 配下）に記録されます。固定したい場合は `obj/` を削除するか `dotnet restore --force-evaluate` を実行します。

## テスト

テストは UI を起動せずに実行できます。

```powershell
# ソリューション全体（7 テストプロジェクト、434 件）
dotnet test --solution ZenHanConverter.slnx

# 個別に実行する場合（プロジェクトは --project で指定する）
dotnet test --project test\App.Core\App.Core.Tests.csproj
dotnet test --project test\App.Core.Presentation\App.Core.Presentation.Tests.csproj
dotnet test --project test\App.MewUI\App.MewUI.Tests.csproj
dotnet test --project test\App.WinUI3\App.WinUI3.Tests.csproj
dotnet test --project test\App.AvaloniaUI\App.AvaloniaUI.Tests.csproj
dotnet test --project test\App.WinForms\App.WinForms.Tests.csproj
dotnet test --project test\parity\tests_parity.csproj
```

**対象の指定には `--solution` / `--project` を使ってください。** xunit.v3 4 系は Microsoft.Testing.Platform（MTP）で
実行され、位置引数（`dotnet test ZenHanConverter.slnx`）はテスト アプリへの引数として扱われて
**0 件・終了コード 5** になります（実行基盤の指定は `global.json` の `test.runner` が単一所有します）。

**WinUI 3 を参照するテスト（`test/App.WinUI3` / `test/parity`）から WinRT（Windows App SDK）を起動しないでください。**
UI 型の実処理（`DispatcherQueue.GetForCurrentThread()` など）は Windows App SDK の初期化を伴い、`CoreMessagingXP.dll` を
読み込みます。UI を持たない CI（非対話セッション）ではこれが**出力を出さないまま停止**します（Windows App Runtime が
インストール済みのローカルでは再現しません）。UI スレッドへの依存は UI 層（`DependencyInjectionExtensions` が生成する
`IUiDispatcher`）からコンストラクターで受け取り、ViewModel 側で静的に取得しないでください。

**`src/App.WinUI3` は Windows App Runtime を同梱しません**（`WindowsAppSDKSelfContained=false`）。代わりに
アセンブリの自動初期化を止め（`WindowsAppSdkBootstrapInitialize=false`）、`Program.Main` が
`EsUtil.Ui.WinUI3.Interop` の `WindowsAppRuntimeBootstrap` で有無の確認と導入を行います。自動初期化を
有効にすると、このアセンブリを読み込むテストが Windows App Runtime の無い CI で導入ダイアログ待ちのまま
停止します。テスト用プロジェクト側は `WindowsAppSdkAutoInitialize=false` で自動初期化を無効にしています。

`test/App.Core` は `src/App.Core` のみを参照し、UI フレームワークに依存しないことを保証します。  
`test/App.Core.Presentation` は `src/App.Core.Presentation` のみを参照し、同じく UI フレームワークに依存しないことを保証します。  
設定ファイルを書き込むテストは、実ユーザーの `%LOCALAPPDATA%` 配下を汚さないよう
自動保存先を一時ディレクトリへ差し替えます。

## 実行

```powershell
# MewUI 版
dotnet run --project src/App.MewUI/App.MewUI.csproj

# WinUI 3 版
dotnet run --project src/App.WinUI3/App.WinUI3.csproj

# Avalonia UI 版
dotnet run --project src/App.AvaloniaUI/App.AvaloniaUI.csproj

# WinForms 版
dotnet run --project src/App.WinForms/App.WinForms.csproj
```

### 配布用ビルド

スクリプトは `buildScript/` に置いてあります。**カレントディレクトリに関係なく**リポジトリルートを基準に動作します。

| スクリプト（`buildScript/` 配下） | 対象 | 方式 | 出力先 | 出力物 |
| --- | --- | --- | --- | --- |
| `publish_MewUI.bat` | MewUI 版 | Native AOT（`aot`） | `publish\mewui-<RID>-aot\` | exe 1 個 |
| `publish_WinUI3.bat` | WinUI 3 版 | Native AOT（`aot`） | `publish\winui3-<RID>-aot\` | exe 1 個 + ネイティブ DLL 3 個 + XAML リソース |
| `publish_AvaloniaUI.bat` | Avalonia UI 版 | Native AOT（`aot`） | `publish\avaloniaui-<RID>-aot\` | exe 1 個 + ネイティブ DLL |
| `publish_WinForms.bat` | WinForms 版 | 自己完結＋単一ファイル（`single`） | `publish\winforms-<RID>-single\` | exe 1 個 |

- 第 1 引数に RID を渡すと対象を変更できます（既定: `win-x64`）。
- MewUI 版は Native AOT のため完全な単一バイナリです。
- WinUI 3 版は発行時に Windows App SDK の未使用ペイロード（AI / ML / Search / Widgets / WebView2）を自動除外します（約 55 MB 削減。詳細は `src/App.WinUI3/SPEC_System.md`）。
- WinUI 3 版は Native AOT で発行します（Windows App Runtime は同梱しないため、exe 単体ではなくフォルダー単位で配布します。詳細は `src/App.WinUI3/SPEC_System.md`）。
- Avalonia UI 版は Native AOT ですが、描画に使う SkiaSharp のネイティブ DLL を exe へ同梱できないため、**exe 単体ではなくフォルダー単位で配布**します（`libSkiaSharp.dll` / `av_libglesv2.dll` / `libHarfBuzzSharp.dll` を同じフォルダーに置きます。詳細は `src/App.AvaloniaUI/SPEC_System.md`）。
- WinForms 版は Native AOT に対応せず、トリミングもサポートされないため（`NETSDK1175`）、単一ファイル化のみを行います（詳細は `src/App.WinForms/SPEC_System.md`）。
- WinUI 3 版の exe 名は `ZenHanConverter.WinUI3.exe`、WinForms 版は `ZenHanConverter.WinForms.exe` です（アセンブリ名は `<製品名>.<UI名>` で、プロジェクト名 `App.WinUI3` とは一致しません。 名前空間は `EsUtil.ZenHanConverter.App.WinUI3` のままです）。
- ここでの出力は展開済みの状態です。**Release に添付する配布物はすべて zip**（`release-config.json` の `uis[].zip`。`release.yml` が publish 後に zip 化）です。

### Release ビルドと実行

発行（publish）を行わず、Release 構成のビルドと実行だけを行うスクリプトです。AOT・単一ファイル化を伴わないため短時間で確認できます。

| スクリプト（`buildScript/` 配下） | 対象 | 出力先 |
| --- | --- | --- |
| `publish_MewUI.bat` | MewUI 版 | `src\App.MewUI\bin\Release\net10.0-windows10.0.17763.0\` |
| `publish_WinUI3.bat` | WinUI 3 版 | `src\App.WinUI3\bin\Release\net10.0-windows10.0.26100.0\win-x64\` |
| `publish_AvaloniaUI.bat` | Avalonia UI 版 | `src\App.AvaloniaUI\bin\Release\net10.0-windows10.0.17763.0\` |
| `publish_WinForms.bat` | WinForms 版 | `src\App.WinForms\bin\Release\net10.0-windows10.0.17763.0\` |

- 生成物はフレームワーク依存のため、実行には .NET 10 ランタイムが必要です。
  - ただし **WinUI 3 版は .NET ランタイムが自己完結**（`SelfContained`）です。 Windows App Runtime は同梱せず、
    起動時に有無を確認して無ければ導入します（実行には出力フォルダーごと必要です）。
- 配布用の exe が必要な場合は `buildScript/publish_<UI>.bat` を使用してください（発行の手順は `buildScript/publish_ui.bat` が単一所有します）。

## CI（ビルドとテスト）

[`workflows/build.yml`](workflows/build.yml) が、`main` / `dev` / `release/**` への push、`main` 向け Pull Request、手動実行のたびに次を実行します（`windows-latest`）。

1. 復元（`dotnet restore <solutionFile> -p:RestoreLockedMode=true`）
2. 検証（`pwsh -NoProfile -File buildScript/verify.ps1 -Solution <solutionFile> -Configuration Release -NoRestore`）
   - ビルド（0 警告 / 0 エラー）、テスト全件、NuGet の健全性、文書とコードの走査（合計 36 項目）
   - 配布物の作成は行いません（リリースの `publish.yml` が担当します）

- 対象のソリューション ファイルは `release-config.json` の `solutionFile` が単一所有します（ワークフローには書きません）。
- **`dev` への push でも実行します**（**開発〜dry-run までの確認は `dev` で行う**ため）。 **`main` へは PR でのみ**反映します（ruleset `Protect main` が直接 push を拒否し、`build` の成功をマージ条件にします）。 **公開（release）は `main` からのみ**です。
- **配布物は作成しません。** publish（Native AOT を含む重い処理）は、手動実行の [`workflows/release.yml`](workflows/release.yml) が
  [`workflows/publish.yml`](workflows/publish.yml) を通じて**リリース時に 1 度だけ**行います。
- GitHub Release への**登録は行いません**。

この実行結果はリリースの**前提（ゲート）**です。`release.yml` は、リリース対象コミットに対する `build.yml` の成功実行が存在することを確認してから Release を作成します（[`RELEASE.md`](RELEASE.md) を参照）。
`README.md` 先頭の build バッジはこのワークフローの状態を表示します。

### ワークフローの分担

| ワークフロー | 実行契機 | 責務 |
| --- | --- | --- |
| [`workflows/publish.yml`](workflows/publish.yml) | `workflow_call`（`release.yml` からのみ） | 全 UI の publish と保管。**UI の定義と publish 手順の単一実装** |
| [`workflows/build.yml`](workflows/build.yml) | `main` / `dev` / `release/**` への push、`main` 向け PR、手動 | ビルドとテスト（リリースのゲート。 **PR では必須チェック `build`**） |
| [`workflows/release.yml`](workflows/release.yml) | 手動のみ | リリース（検証 → 全 UI の publish → バージョンコミット → タグ + Release 作成） |

UI を追加・変更する場合は、**`release-config.json` の `uis` を編集するだけ**です（ワークフローの変更は不要）。

publish と同等の条件（クリーンな状態）で事前確認する場合:

```powershell
git clean -xdf -- src test
dotnet restore ZenHanConverter.slnx
dotnet build ZenHanConverter.slnx -c Release --no-restore
dotnet test --solution ZenHanConverter.slnx -c Release --no-build
```
