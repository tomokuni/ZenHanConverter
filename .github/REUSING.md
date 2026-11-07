# 他のリポジトリへの流用方法

本ドキュメントは、本リポジトリの**ビルド・テスト・リリースの仕組みを別のリポジトリへ展開する**開発者向けの情報です。
仕組みの設計と使い方は [`RELEASE.md`](RELEASE.md) を参照してください。

## 何が流用できるか

**リポジトリ固有の知識を `release-config.json` と `buildScript/publish_*.bat` に閉じ込めています。** それ以外は汎用です。

| ファイル | 流用 | 備考 |
| --- | --- | --- |
| [`release-config.json`](release-config.json) | **コピーして編集** | 唯一のリポジトリ固有設定 |
| [`actions/read-config`](actions/read-config/action.yml) | **コピーして調整** | `release-config.json` を読んで各ワークフローへ渡す共通アクション。 **キーを増やす場合は `outputs` にも追加する** |
| [`../global.json`](../global.json) | **コピーして編集** | .NET SDK のバージョン（対象フレームワークに合わせる） |
| [`scripts/version.ps1`](scripts/version.ps1) | **そのまま** | バージョンの規則（リポジトリ非依存） |
| [`scripts/set-version.ps1`](scripts/set-version.ps1) | **そのまま** | バージョンファイルのパスは引数・設定で渡す |
| [`scripts/verify-release-version.ps1`](scripts/verify-release-version.ps1) | **そのまま** | ブランチ規則は `releaseBranches` が持つため変更不要 |
| [`scripts/show-current-versions.ps1`](scripts/show-current-versions.ps1) | **コピーして調整** | 現在のバージョン状況を表示する（読み取りのみ）。 配布物の表示方法のみリポジトリ依存 |
| [`dependabot.yml`](dependabot.yml) | **コピーして調整** | GitHub Actions / NuGet の更新 PR。 `directories` を対象プロジェクトに合わせる |
| [`rulesets/tag-version.json`](rulesets/tag-version.json) | **コピー（編集不要）** | Tag ruleset の定義。 リポジトリの Settings へインポートする |
| [`workflows/publish.yml`](workflows/publish.yml) | **コピーして調整** | `uis[]` を読んで配布物を作る。 `.bat` の呼び出しと zip 化はこのリポジトリ固有 |
| [`workflows/release.yml`](workflows/release.yml) | **そのまま** | 設定を読むため変更不要（配布物はファイル名のまま添付する） |
| [`workflows/build.yml`](workflows/build.yml) | **コピーして編集** | 実行環境（`windows-latest`）のみリポジトリ依存 |
| `buildScript/publish_*.bat` | **新規作成** | UI ごとの publish 手順 |
| 本ドキュメント・`RELEASE.md` | コピーして調整 | |

## 手順

### 1. ファイルをコピーする

```text
<新しいリポジトリ>/
├── Directory.Build.props     # リリースバージョン（新規作成。下記「2.」の versionFile が指す）
├── global.json               # .NET SDK のバージョン（新規作成。ワークフローでは指定しない）
├── .github/
│   ├── release-config.json
│   ├── actions/
│   │   └── read-config/
│   │       └── action.yml
│   ├── dependabot.yml
│   ├── RELEASE.md
│   ├── REUSING.md
│   ├── rulesets/
│   │   └── tag-version.json
│   ├── scripts/
│   │   ├── version.ps1
│   │   ├── set-version.ps1
│   │   ├── verify-release-version.ps1
│   │   └── show-current-versions.ps1
│   └── workflows/
│       ├── build.yml
│       ├── publish.yml
│       └── release.yml
└── buildScript/
    └── <UI ごとの publish スクリプト>.bat
```

`Directory.Build.props` はリポジトリルートに置く最小構成でかまいません。

```xml
<Project>

  <PropertyGroup>
    <!-- リリースバージョン。各 .csproj では指定しない。 -->
    <!-- 自動インクリメントは行わない。リリース時に release.yml が入力値へ更新する。 -->
    <Version>0.0.1</Version>
  </PropertyGroup>

</Project>
```

既存の共通プロパティ（`Nullable` / `ImplicitUsings` / `LangVersion` など）をここへまとめてもかまいません。

`global.json` には対象フレームワークに合う SDK を記載します（ワークフローには書きません）。

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature"
  }
}
```

テストの実行基盤によって `global.json` と `build.yml` の書き方が変わります。

| 実行基盤 | `global.json` | `build.yml` のテスト |
| --- | --- | --- |
| VSTest（`Microsoft.NET.Test.Sdk`。 **本リポジトリ**） | `test.runner` は不要 | `dotnet test '<ソリューション>' -c Release --no-build` |
| Microsoft.Testing.Platform（xunit.v3 4 系） | `"test": { "runner": "Microsoft.Testing.Platform" }` | `dotnet test --solution '<ソリューション>' -c Release --no-build` |

MTP を選んだ場合、`test.runner` を指定しないと VSTest モードで実行され、次のエラーになります。

```text
error: Testing with VSTest target is no longer supported by Microsoft.Testing.Platform on .NET 10 SDK and later.
```

MTP モードではテスト対象を `--solution` / `--project` で指定します（位置引数を渡すとテスト アプリへの引数として扱われ、0 件・終了コード 5 になります）。

### 2. `release-config.json` を編集する

| キー | 内容 | 例 |
| --- | --- | --- |
| `product` | リリース名とアセットのタイトルに使う表示名 | `"ZenHanConverter"` |
| `versionFile` | バージョン（`<Version>`）を記載するファイル（リポジトリルートからの相対パス） | `"Directory.Build.props"` |
| `solutionFile` | ビルド・テストの対象となるソリューションファイル（リポジトリルートからの相対パス） | `"ZenHanConverter.slnx"` |
| `gateWorkflow` | リリースの前提（ゲート）となるワークフローのファイル名 | `"build.yml"` |
| `releaseBranches` | **リリースを許可するブランチ**（完全一致とワイルドカード。 未指定は `main` のみ。 `release/` 配下は `release/<major>.<minor>` の形式に限定される） | `["main"]` / `["main", "release/**"]` |
| `artifactRetentionDays` | アーティファクトの保持日数 | `30` |
| `releaseNotes` | Release 本文の冒頭に付ける説明 | `"..."` |
| `uis[]` | 配布する UI の定義（下記） | — |

`uis[]` の各要素:

| キー | 内容 |
| --- | --- |
| `name` | アーティファクト名（ジョブの表示にも使う） |
| `script` | publish スクリプト（リポジトリルートからの相対パス。 例: `buildScript/publish_MewUI.bat`） |
| `output` | publish の出力パス（複数ファイルの場合はフォルダ） |
| `asset` | Release に添付するファイル名（zip する場合は作成後の名前。 例: `ZenHanConverter.MewUI.zip`） |
| `zip` | 配布前に zip 化する場合は `true`。 `output` が単一ファイル（exe）でもフォルダ（exe + ネイティブ DLL）でも同じ指定で扱える |
| `requiresWindowsAppRuntime` | 配布物が Windows App Runtime へフレームワーク依存で動く場合は `true`（`publish.yml` が配布物の起動検証の前に [`buildScript/installWindowsAppRuntime.ps1`](../buildScript/installWindowsAppRuntime.ps1) を実行する。 **版は `Directory.Packages.props` の `Microsoft.WindowsAppSDK` から導く**） |

**UI を増やす場合はこの配列に要素を追加します。** ワークフローとスクリプトの変更は不要です。

設定の値は [`actions/read-config`](actions/read-config/action.yml) がまとめて読み取ります。
**キーを追加する場合は、同アクションの `outputs` にも追加してください**（追加しないとワークフローから参照できません）。
**必須のキー（`product` / `versionFile` / `solutionFile` / `gateWorkflow` / `uis`）が空の場合、読み取り時に失敗します**
（ワークフロー側で原因の分かりにくいエラーになるのを防ぐため）。

> **注意**: バージョンはリポジトリルートの `Directory.Build.props` に `<Version>` として記載し、
> 各 `.csproj` には記載しないでください（全プロジェクトが同じ値を継承します）。

### 3. `build.yml` を編集する

リポジトリに合わせて変更するのは**実行環境だけ**です（対象のソリューションファイルは `solutionFile` が持ちます）。

- Windows 固有のターゲット（WinUI 3 / WPF / Windows Forms など）を含む場合は `runs-on: windows-latest`。
- クロスプラットフォームのみの場合は `runs-on: ubuntu-latest`（`publish.yml` も `windows-latest` から変更する）。

`publish` は push では実行しません（配布物の作成はリリース時に 1 度だけ）。 ゲートはビルドとテストの成功です。

テストの実行コマンドはテスト フレームワークに依存します（Windows 固有のターゲットを含む場合は `runs-on: windows-latest` のままにします）。

- **xunit.v3 4 系（Microsoft.Testing.Platform）**: `dotnet test --solution <ソリューション>`（対象は `--solution` / `--project` で指定する）
- **VSTest（`Microsoft.NET.Test.Sdk`）**: `dotnet test <ソリューション>`

### 4. publish スクリプトを用意する

UI ごとに `buildScript/publish_<名前>.bat` を作成します。 仕様は次のとおりです。

- **発行方式（Single AOT / Single / EXE）の選択は [`buildScript/publish_ui.bat`](../buildScript/publish_ui.bat) が単一所有する**。 UI ごとのスクリプトは、その UI のプロジェクトを渡して呼ぶ薄い入口にする（`call "%~dp0publish_ui.bat" <UI 名小文字> "<プロジェクトの相対パス>" %*`）。 方式ごとの可否と実測は **`RELEASE.md` が持つ**（本リポジトリの例）。
- **スクリプトの場所（`buildScript/`）からリポジトリルートへ移動してから実行する**（`pushd "%~dp0.."`）。 これにより、呼び出し元のカレントディレクトリに依存しません。
- **バッチ ファイルは ASCII だけで書く**（`cmd.exe` はバッチ ファイルをコンソールのコードページで読み、読み取り位置を復号後の文字数で数えるため、日本語は化けと行のずれを起こします）。 **`setlocal` の直後に `chcp 65001 >nul` を置く**（ASCII なら安全であり、`dotnet` が出力する UTF-8 のログが読めます）。 根拠と検査は [`検証手順.md`](検証手順.md) §19
- 成功時は終了コード 0、失敗時は非 0 で終了する
- 出力先は `release-config.json` の `output` と一致させる（方式が名前に入るため、方式が変わったら `output` も更新する）
- 他のスクリプトを呼ぶ場合は、カレントディレクトリではなく `%~dp0` を基準にする（例: `call "%~dp0publish_MewUI.bat"`）

[buildScript/publish_AvaloniaUI.bat](../buildScript/publish_AvaloniaUI.bat) と [buildScript/publish_MewUI.bat](../buildScript/publish_MewUI.bat) が参考になります。

### 5. `show-current-versions.ps1` を調整する

`release.yml` のドライラン（バージョン未入力時）が、このスクリプトで現在のバージョン状況を実行サマリーへ表示します。
本リポジトリは「最新の GitHub Release に添付されている配布物」を表示しますが、配布先（NuGet.org など）が異なる場合は
その公開状況の表示に置き換えます。 **読み取りのみ**を行い、状態を変更しないようにしてください。

### 6. 動作を確認する

```powershell
# スクリプトの単体確認（バージョン設定・検証。既定のバージョンファイルは Directory.Build.props）
Copy-Item Directory.Build.props "$env:TEMP/dbp.bak"
& .github/scripts/set-version.ps1 -Version 0.0.2
& .github/scripts/verify-release-version.ps1 -Version 0.0.3 -Branch main
& .github/scripts/show-current-versions.ps1 -Branch main -NoNetwork
Copy-Item "$env:TEMP/dbp.bak" Directory.Build.props

# パイプラインの確認（CI と同一条件）
git clean -xdf -- src test
dotnet restore <ソリューション>.slnx
dotnet build <ソリューション>.slnx -c Release --no-restore
dotnet test --solution <ソリューション>.slnx -c Release --no-build
```

その後、`main` へ push して Build が成功することを確認し、`Actions` → `Release` を手動実行します
（最初は `version` を空欄にしてドライランを行い、現在のバージョン状況を確認します）。

## 流用時に必要になる可能性がある変更

| 状況 | 対応 |
| --- | --- |
| **リポジトリが private** | Actions の分数が有料になります。 リリース時の publish は実行時間が長いため、`uis[]` を減らすか、`build.yml` の `on.push` に `paths` を追加してゲートの実行回数を抑えることを検討してください |
| **NuGet ギャラリー未公開のパッケージを参照する** | クリーンな CI からは復元できません。 リポジトリへ同梱し `NuGet.config` のソースに追加するか、公開してください |
| **テスト ランナーに MTP を使う** | xunit.v3 4 系へ上げて `global.json` に `test.runner: Microsoft.Testing.Platform` を追加し、`build.yml` のテストを `dotnet test --solution <ソリューション>`（`--solution` 指定）へ変えます。 **本リポジトリは VSTest（`Microsoft.NET.Test.Sdk`）のため対応不要です** |
| **ビルドをもっと速くしたい** | `.NET SDK` は**ランナー イメージの最新版**をそのまま使います（`actions/setup-dotnet` を使わない）。 イメージの SDK は PATH が通っており、`global.json` は `rollForward: latestFeature` のためイメージの最新 SDK で要件を満たします。 `setup-dotnet` を使うと「チャネルの最新」を取得しようとしてイメージに無い版を毎回ダウンロードします。 キャッシュは **NuGet の復元だけ**を対象にします（`build.yml` は共通の 1 つ、`publish.yml` は UI ごと）。 残るコストは NuGet キャッシュの復元（約 1.9GB / 約 41 秒）とビルド（約 45 秒）です。 前者は `Avalonia.Skia` が不要なプラットフォームのネイティブ アセット（Linux / WebAssembly で約 480MB）を無条件に要求するため削減できません。 後者は `bin` / `obj` をソース ハッシュでキャッシュすれば短縮できますが、**キャッシュが約 2GB 増えて復元コストと相殺する**ため採用していません |
| **複数系列の保守（バックポート）が不要** | `releaseBranches` を `["main"]` にし、`build.yml` の `release/**` トリガーを外します |
| **バージョンを自動で決めたい** | 本仕組みは「人が入力する」前提です。 自動化（Conventional Commits からの算出など）を併用する場合は、`verify-release-version.ps1` の検証はそのまま活かせます |
| **NuGet などにも配布したい** | `release-config.json` に項目を足し、`release.yml` の `release` ジョブへ公開ステップを追加します。 配布物は `publish.yml` の `pack` 相当（`dotnet pack`）に置き換えます |
| **タグの保護（Tag ruleset）を入れる** | `rulesets/tag-version.json` をそのまま使えます（`v*` の作成・更新・削除を禁止し、bypass に GitHub Actions を指定）。 Settings → Rules → Rulesets → **Import a ruleset** で読み込んでください。 手順は `RELEASE.md` の「リポジトリの設定（初回のみ）」を参照 |

## 変更時の注意事項

- **UI の定義（`uis[]`）は `release-config.json` に置いてください。** ワークフローへ書き戻すと二重管理になり、追加時に漏れます。
- **ビルド対象のソリューションファイル（`solutionFile`）は `release-config.json` に置いてください。** ワークフローへソリューション名を書くと、リポジトリごとにワークフローが分かれます。
- **設定の読み取りは `actions/read-config` に置いてください。** ワークフローごとに `jq` や `Get-Content` で読み直すと、キーを追加したときに読み取り漏れが起きます。
- **.NET SDK のバージョンは `global.json` に置いてください。** ワークフローへ `dotnet-version` を書くと二重管理になり、更新時にずれます。
- **テストの実行基盤（VSTest / MTP）も `global.json` と `build.yml` のどちらか一方でだけ決めてください**（両方に書くとずれます）。
- **バージョンの規則（形式・比較・系列・タグの列挙）は `scripts/version.ps1` に置いてください。** 他のスクリプトで再実装すると判定がずれます。
- **バージョンを記載するファイルは 1 つにしてください**（本リポジトリは `Directory.Build.props`）。 番号と成果物が不一致になるのを防ぎます。
- **配布物の作成（publish）は、タグ作成とバージョンコミットより前に実行してください。** 失敗してもタグとバージョンを消費しないようにします。
- **タグは `gh release create --target` に作成させてください**（配布物の作成が成功した後にのみタグが作られます）。
- **`releaseBranches` のワイルドカードに頼りすぎないでください。** `release/` 配下は `release/<major>.<minor>` に限定され、入力バージョンの系列と一致していることも `verify-release-version.ps1` が検証します。
- **配布物を作る重い処理を push に戻さないでください。** ゲートはビルドとテストの成功であり、配布物が作成できることはリリース実行時に検証されます（`RELEASE.md` の「失敗した場合の復旧」を参照）。

## 複数リポジトリでの共通化（同じ CI を使う）

**`.github/workflows/*.yml` と `.github/rulesets/*.json` は、リポジトリごとに書き分けません。** 次のリポジトリは
**配布物を作る呼び出し先（`publish.yml` / `pack.yml`）とその公開処理を除いて同じファイル**を使い、
リポジトリ固有の値だけを `.github/release-config.json` に置きます（新しいリポジトリも同じ形で足せます）。

| リポジトリ | 種類 | 配布物の作り方 |
| --- | --- | --- |
| SaveEvidence | アプリ（4 UI の実行ファイル） | `publish.yml` が `buildScript/publish_*.bat` で publish する |
| ZenHanConverter | アプリ | 同上（`publish.yml`） |
| EsUtil | ライブラリ（NuGet パッケージ） | `pack.yml` が `dotnet pack` で作る |

**共通の契約**（どのリポジトリでも同じ）:

| 項目 | 内容 |
| --- | --- |
| 必須チェック | **`ci`**（`build.yml` の集約ジョブ）。 ruleset `Protect main` が要求する |
| `build.yml` の実行契機 | **`main` 向け Pull Request と手動実行だけ**（push では実行しない） |
| 検証の中身 | `buildScript/verify.ps1`（ビルド・テスト・リポジトリ全体の検査）と `buildScript/verifyDocs.ps1`（文書とコードの走査） |
| `locks.yml` | `main` 向け PR で `buildScript/updateLocks.ps1` を実行し、`packages.lock.json` の差分を自動コミットする（`GITHUB_TOKEN` の push では `pull_request` が起動しないため、`build.yml` を `workflow_dispatch` で起動し直す） |
| 走査（`docs` ジョブ） | `buildScript/verifyDocs.ps1`。 **ubuntu で実行する**（`verifyDocs.ps1` が npx を Linux では直接起動し、改行コードは index を見る）。 **文書の型参照検査のため復元だけ行う**（参照先パッケージの XML ドキュメントを読む） |
| `release.yml` | Pull Request では**前提検証だけ**（版・タグ・CHANGELOG・ブランチ）。 手動実行の `mode` で `dry-run` / `release` を選ぶ |
| rulesets | [`rulesets/protect-main.json`](rulesets/protect-main.json)（`ci` を必須。 strict）と [`rulesets/tag-version.json`](rulesets/tag-version.json) |
| 設定の単一所有 | `.github/release-config.json`（製品名・ソリューション・ブランチ・UI の定義）と [`.github/actions/read-config`](actions/read-config/action.yml) |

**リポジトリごとに変えてよいのは次の 3 点だけ**です（それ以外はコピーします）。

- `release-config.json` の値（`product` / `solutionFile` / `uis` など）
- 配布物の作り方（アプリは `publish.yml`、ライブラリは `pack.yml`）
- `release.yml` の**配布物を作るジョブ**（呼び出し先の `publish.yml` / `pack.yml` の名前と、NuGet へ公開するかどうか）。 **前提検証・版・タグ・CHANGELOG の検証は共通**です
