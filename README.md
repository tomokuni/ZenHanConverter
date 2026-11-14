# ZenHanConverter

**全角 / 半角変換ユーティリティ** — コピーした文字列を、設定した規則で自動的に全角 / 半角へ変換します。

![Release](https://github.com/tomokuni/ZenHanConverter/actions/workflows/release.yml/badge.svg)
![Build](https://github.com/tomokuni/ZenHanConverter/actions/workflows/build.yml/badge.svg?event=pull_request)
![Platform](https://img.shields.io/badge/Platform-Windows%2010%2B-0078D6)
![.NET](https://img.shields.io/badge/.NET-11.0-512BD4)
![Avalonia](https://img.shields.io/badge/UI-Avalonia-8D46D6)
![MewUI](https://img.shields.io/badge/UI-MewUI-00BFFF)
![WinUI3](https://img.shields.io/badge/UI-WinUI%203-0078D4)
![WinForms](https://img.shields.io/badge/UI-WinForms-512BD4)
![License](https://img.shields.io/badge/License-MIT-3DA639)

---

## 目次 <!-- omit from toc -->

- [1. 概要](#1-概要)
- [2. 機能一覧](#2-機能一覧)
- [3. システム要件](#3-システム要件)
- [4. インストール方法](#4-インストール方法)
  - [4.1 配布物のダウンロード](#41-配布物のダウンロード)
  - [4.2 ソースからのビルド](#42-ソースからのビルド)
  - [4.3 検証（変更をコミットする前）](#43-検証変更をコミットする前)
- [5. クイックスタート](#5-クイックスタート)
- [6. 設定ファイルとログ](#6-設定ファイルとログ)
- [7. アーキテクチャとプロジェクト構成](#7-アーキテクチャとプロジェクト構成)
- [8. 旧版の設定の引き継ぎ](#8-旧版の設定の引き継ぎ)
- [9. ドキュメント](#9-ドキュメント)
- [10. トラブルシューティングと問い合わせ](#10-トラブルシューティングと問い合わせ)
- [11. 開発への参加](#11-開発への参加)
- [12. ライセンス](#12-ライセンス)

---

## 1. 概要

ZenHanConverter は、クリップボードの文字列を監視し、**数字・英字・記号・かな・約物・タブ / 改行・連続スペース**を設定どおりの全角 / 半角へ変換する Windows アプリです。  コピーするだけで変換されるため、貼り付け先の書式に合わせ直す手作業が要りません。

**Avalonia UI / MewUI / WinUI 3 / WinForms の 4 つの UI 実装**を持ち、変換ロジックと設定を共有します。  設定ファイルは 4 つで共通、ログは実行ファイルごとに分かれます。

| コピー元 | 変換後（例） |
| --- | --- |
| `２０２４年ＡＢＣ株式会社` | `2024年ABC株式会社`（数字・英字を半角） |
| `ｺﾝﾆﾁﾊｾｶｲ` | `こんにちはせかい`（半角カナを全角ひらがな） |

## 2. 機能一覧

詳細（設定項目・変換例・プリセット）は各 UI の README が持ちます。

| 機能 | 内容 | 詳細 |
| --- | --- | --- |
| クリップボード変換 | コピーした文字列を自動変換（タイトルバーのスイッチで有効 / 無効） | [Avalonia UI](src/App.AvaloniaUI/README.md) · [MewUI](src/App.MewUI/README.md) · [WinUI 3](src/App.WinUI3/README.md) · [WinForms](src/App.WinForms/README.md) |
| 変換カテゴリ | 数字 / 英字 / 記号・かな（半角カナ / 全角カタカナ / 全角ひらがな）・かな約物・円記号 / バックスラッシュ・タブ / 改行・連続スペース | 同上 |
| 置換ルール | ユーザー定義の検索 / 置換（正規表現可。  登録順に適用） | 同上 |
| プリセット | 設定の保存・呼び出し（組み込みプリセット 2 種を含む）・設定のエクスポート / インポート | 同上 |
| 画面 | ホーム（変更前 / 変更後の 2 分割。  分割バーで比率変更）・設定 | 同上 |

## 3. システム要件

| 項目 | 要件 |
| --- | --- |
| **OS** | Windows 10（15063+）以降（WinUI 3 版は 10.0.18362+） |
| **.NET ランタイム** | .NET 11.0 以降（自己完結の配布物は同梱のため不要） |
| **アーキテクチャ** | x64 |

## 4. インストール方法

### 4.1 配布物のダウンロード

[Releases](https://github.com/tomokuni/ZenHanConverter/releases) の zip を展開し、同じフォルダーの中身をまとめて配置してください。

| UI | 実行ファイル | 構成 |
| --- | --- | --- |
| Avalonia UI 版 | `ZenHanConverter.AvaloniaUI.exe` | exe + ネイティブ DLL（Native AOT） |
| MewUI 版 | `ZenHanConverter.MewUI.exe` | exe + ネイティブ DLL（Native AOT） |
| WinForms 版 | `ZenHanConverter.WinForms.exe` | exe 1 個（自己完結・単一ファイル） |
| WinUI 3 版 | `ZenHanConverter.WinUI3.exe` | **AOT 版**: exe + ネイティブ DLL / `.pri`（Windows App Runtime が必要） / **Single 版**: exe 1 個（Windows App Runtime と .NET を同梱） |

### 4.2 ソースからのビルド

```powershell
git clone https://github.com/tomokuni/ZenHanConverter.git
cd ZenHanConverter
# UI ごとの配布物を作る（方式は可能なものが自動で選ばれる）
cmd /c buildScript\publish_AvaloniaUI.bat
cmd /c buildScript\publish_MewUI.bat
cmd /c buildScript\publish_WinUI3.bat
cmd /c buildScript\publish_WinForms.bat
```

出力先は `publish/<UI>-win-x64-<方式>` です（方式の選択と制約は [`.github/RELEASE.md`](.github/RELEASE.md) が単一所有します）。

### 4.3 検証（変更をコミットする前）

```powershell
pwsh -NoProfile -File .github/verify/verify.ps1
```

ビルド・テスト・文書・配布物の起動をまとめて確かめます。  合否基準は [`.github/検証手順.md`](.github/検証手順.md) が単一所有します。

## 5. クイックスタート

1. 使う UI の実行ファイルを起動します（前回のウィンドウ位置・サイズが復元されます）。
2. タイトルバーの「クリップボード変換」スイッチを有効にします。
3. 任意のアプリで文字列をコピーすると、変換後の文字列がクリップボードへ入ります（`Ctrl + V` で貼り付け）。
4. 変換規則は設定画面でカテゴリごとに選びます。  よく使う組み合わせはプリセットとして保存できます。

## 6. 設定ファイルとログ

設定は `%LOCALAPPDATA%\ZenHanConverter\` 配下へ JSON 形式で保存されます。  **4 つの UI は同じ設定を共有**します。

| ファイル | 内容 |
| --- | --- |
| `AppSetting.json` | ウィンドウ位置・サイズ、クリップボード変換の有効 / 無効 |
| `Settings.json` | 変換カテゴリごとの変換方向・置換ルール |
| `Presets\{プリセット名}.json` | ユーザー定義プリセット |

- 設定は変更から 300 ミリ秒後に自動保存されます（終了時は即時保存）。
- **ログ**（実行ファイルごと）: `%LOCALAPPDATA%\ZenHanConverter\logs\<アセンブリ名>.<yyyyMMdd>.log`（日次ローテーション・7 日保持）。

## 7. アーキテクチャとプロジェクト構成

UI 非依存のコアと提示層（Presentation）を 4 つの UI が共有します。  依存は **UI → Presentation → Core** の一方向です。

| プロジェクト | 役割 | ドキュメント |
| --- | --- | --- |
| `src/App.Core` | 変換ロジック・モデル・カテゴリ定義・Win32 API・アイコン | [`README.md`](src/App.Core/README.md) |
| `src/App.Core.Presentation` | UI 非依存の ViewModel（設定画面）と共有表示文言 | [`README.md`](src/App.Core.Presentation/README.md) |
| `src/App.AvaloniaUI` / `App.MewUI` / `App.WinUI3` / `App.WinForms` | 各 UI 版アプリ | 各プロジェクトの `README.md` |
| `test/App.*` | 単体テスト（`test/parity` は 4 UI の挙動の一致を検証） | [`test/parity/README.md`](test/parity/README.md) |

## 8. 旧版の設定の引き継ぎ

設定の保存先は `%LOCALAPPDATA%\ZenHanConverter` です。 **旧版の保存先（`%LOCALAPPDATA%\ClipboardZenHanConverter`）がある場合は、初回起動時にその内容を新しい保存先へコピーします**（上書きはしません。 旧フォルダーは残るため、確認後に削除してください）。

## 9. ドキュメント

| 文書 | 役割 |
| --- | --- |
| 各プロジェクトの `README.md` / `docs/SPEC_*.md` | 利用仕様・API・システム仕様 |
| [`.github/`](.github) | 規約（コーディング・命名・テスト・ログ・文書・コミット・検証・リリース） |
| [`CHANGELOG.md`](CHANGELOG.md) | 変更履歴 |

## 10. トラブルシューティングと問い合わせ

| 問題 | 原因と対処 |
| --- | --- |
| コピーしても変換されない | タイトルバーの「クリップボード変換」が有効か確認してください。  セキュリティ ソフトやリモート デスクトップがクリップボードを仲介している場合は監視できません。 |
| 特定の文字だけ変換されない | そのカテゴリの変換方向が「なし」になっていないか、置換ルールが元に戻していないかを確認してください（置換ルールは登録順）。 |
| 設定ファイルが壊れた | `AppSetting.json` と `Settings.json` を削除して再起動すると、既定値で再作成されます。 |
| 配布物の exe が起動しない | zip を展開したフォルダーごと配置してください（ネイティブ DLL・`resources.pri` が必要です）。  WinUI 3 の AOT 版は Windows App Runtime が必要で、無ければ起動時に案内して導入します。 |
| 不具合の調査 | 上記のログを添えて [Issues](https://github.com/tomokuni/ZenHanConverter/issues) へ報告してください。  起動時に版・環境が記録されます。 |

## 11. 開発への参加

- 開発は `dev` ブランチで行い、Pull Request で `main` へマージします（[`.github/コミット規約.md`](.github/コミット規約.md)）。
- コミット前に [`.github/verify/verify.ps1`](.github/verify/verify.ps1) を通してください（[`.github/検証手順.md`](.github/検証手順.md)）。
- 貢献の手順は [`.github/CONTRIBUTING.md`](.github/CONTRIBUTING.md) を参照してください。

## 12. ライセンス

[MIT License](LICENSE)
