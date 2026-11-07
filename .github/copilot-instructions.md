# Custom Instructions

本リポジトリの規約は**主題ごとに 1 文書**へ分けている。 本ドキュメントは**索引**であり、規則を重複して記載しない（規則の所有者は各文書である）。

## 1. 規約の一覧（主題ごとの所有者）

| 主題 | 所有者 |
| --- | --- |
| コーディング（設計原則・記述スタイル・型設計・性能・非同期/UI・コメント・文章作法） | [`.github/コーディング規約.md`](コーディング規約.md) |
| 命名（名前空間・フォルダ・ファイル・型・メンバー・ログ名・テスト名・用語） | [`.github/命名規約.md`](命名規約.md) |
| テスト（xUnit v3・構造・網羅・ヘルパー・実行） | [`.github/テスト規約.md`](テスト規約.md) |
| ログ（レベル・接頭辞・所有者・書式・定義） | [`.github/ログ規約.md`](ログ規約.md) |
| ドキュメント（README / SPEC_ExtFunc / SPEC_System の役割・配置・更新・記載範囲） | [`.github/ドキュメント規約.md`](ドキュメント規約.md) |
| 依存関係（プロジェクト参照の許可範囲） | [`.github/依存関係.md`](依存関係.md) |
| ビルド設定（MSBuild プロパティ・TFM・抑止の一覧） | [`.github/ビルド設定規約.md`](ビルド設定規約.md) |
| 例外処理（送出・捕捉・翻訳） | [`.github/例外処理規約.md`](例外処理規約.md) |
| 検証（ビルド・テスト・lint の手順と合否基準） | [`.github/検証手順.md`](検証手順.md) |
| コミット（メッセージ・ブランチ・タグ） | [`.github/コミット規約.md`](コミット規約.md) |
| リファクタリングの指針（よくある指摘） | [`.github/リファクタリング指針.md`](リファクタリング指針.md) |
| リリース（CI・バージョン・配布物） | [`.github/RELEASE.md`](RELEASE.md) |
| 他のリポジトリへの流用 | [`.github/REUSING.md`](REUSING.md) |
| プロジェクト構成と層の帰属 | [`../README.md`](../README.md) |

## 2. GitHub Copilot の動作に関する指示

- 回答は日本語で行う。
- GitHub に Pull Request は行わない。
- ファイルや出力を生成・編集する場合、文字コードは UTF-8 を使う。
- C# / .NET の専門家の視点で回答する。
- **提案が複数ある場合は、おすすめ順で並べて提示する**。
- コード変更は、Model / ViewModel / View の複数レイヤーを修正する必要がある場合でも実施する。
- **作業の前に、対象の規約（上表）を読む**。 規約と実装が食い違う場合は、**規約を先に更新してから**実装を直す。
- **実測してから書く**（推測でドキュメント・規約を書かない）。
- **検証は `pwsh -NoProfile -File buildScript/verify.ps1` を 1 回実行する**（合否基準は [`.github/検証手順.md`](検証手順.md) が所有する。 手順を手作業で書き起こさない）。

## 3. このワークスペース/ソリューションに固有の事項

**本節は「このリポジトリの事実（〜である）」だけを持つ**。 規範（〜する）は各規約が所有する（分担は [`ドキュメント規約.md`](ドキュメント規約.md) が定める）。

### 3.1 ソリューションの構成

ZenHanConverter は Windows デスクトップの全角/半角変換アプリである（実行ファイルは Avalonia UI 版・MewUI 版・WinUI 3 版・WinForms 版の 4 つ）。

**再利用可能な層（共通基盤・変換の定義・UI コントロール）は隣のリポジトリ [`EsUtil`](../../EsUtil/README.md) へ分離されている**。 本ソリューションはそれを**公開パッケージ（`PackageReference`）**で参照する（版は `EsUtilVersion` が単一所有する。 [`.github/依存関係.md`](依存関係.md) §2）。

| 区分 | プロジェクト | 所在 |
| --- | --- | --- |
| 共通基盤（可搬） | `EsUtil.Platform.Core` / `EsUtil.Feature.ZenHan.Core` / `EsUtil.Feature.ColumnPacking.Core` | `../EsUtil` |
| UI コントロール（可搬） | `EsUtil.Ui.WinUI3.ColumnPacking` | `../EsUtil` |
| 共有層（製品固有） | `App.Core` / `App.Core.Presentation` | 本リポジトリ |
| アプリ | `App.AvaloniaUI` / `App.MewUI` / `App.WinUI3` / `App.WinForms` | 本リポジトリ |

| 知りたいこと | 所有者 |
| --- | --- |
| プロジェクト間の参照の許可 | [`依存関係.md`](依存関係.md) |
| 名前空間・フォルダ・アセンブリ名の対応 | [`命名規約.md`](命名規約.md) |
| ログの接頭辞 | [`ログ規約.md`](ログ規約.md) |
| 対象フレームワークの割り当て | [`ビルド設定規約.md`](ビルド設定規約.md) §4 |

### 3.2 このリポジトリ固有の事実

- **リポジトリは 2 つで、ワークスペースへ両方を載せる**（[`../ZenHanConverter.code-workspace`](../ZenHanConverter.code-workspace) が `../EsUtil` を含む）。 ライブラリ側の規約は [EsUtil の作業指示](../../EsUtil/.github/copilot-instructions.md) が所有する。
- **実行ファイルの名前は `<製品名>.<UI名>` とする**（`App.MewUI` → `ZenHanConverter.MewUI.exe`。 `<AssemblyName>` で指定する。 配布物の zip も同名の `ZenHanConverter.MewUI.zip` にし、exe と揃える）。 ライブラリはプロジェクト名と一致させる（`App.Core` → `ZenHanConverter.App.Core`）。
- **文字コードは UTF-8（BOM なし）**で統一する（[`.editorconfig`](../.editorconfig) の `charset = utf-8`）。
- **作業ツリーの改行コードは CRLF**（[`.editorconfig`](../.editorconfig) の `end_of_line = crlf`）。
- **設定の保存先は `%LOCALAPPDATA%\ZenHanConverter`** である（`App.Core` の `AppPaths` が単一所有する。 旧名のフォルダーからは初回起動時に移行する）。
- **CI は GitHub Actions で実行する**（`main` 向け PR と手動実行。 **push では実行しない**。 必須チェックは集約ジョブ `ci`）。 **公開（release）は `main` からのみ**行う（dry-run は `dev` でも実行できる）。
- **`EsUtil` 側の検証は EsUtil リポジトリで実行する**（本リポジトリの走査は `src/` と `test/` 配下だけを対象にし、EsUtil の文書は走査しない）。
