---
name: zenhan-add-ui
description: ZenHanConverter に画面・機能を追加するときの順序（置き場所を決める → 共有 ViewModel → 4 UI の View → テスト → 文書 → 1 回だけ検証）。「画面を追加する」「機能を追加する」「設定の項目を増やす」「4 UI へ広げる」のときに使う。手順の本文は持たず README の §7 と各規約を指す。
---

# 画面・機能追加の順序（規則は持たない）

## 前提

- 構成と層の所有者は [README.md](../../../README.md) の §7（アーキテクチャとプロジェクト構成）である。
- 変換の規則の所有者は隣のリポジトリ `EsUtil` の `EsUtil.Feature.ZenHan.Core`（本リポジトリで書き直さない。 [依存関係.md](../../依存関係.md) §3）。

## 手順

1. 置き場所を `zenhan-layer-placement` の順序で決める。
2. [README.md](../../../README.md) §7 を読む。
3. 共有の ViewModel → UI の View → テストの順に作る（UI の数と構成は 2 で読んだ §7 に従う。 テストの置き場所と対象は [テスト規約.md](../../テスト規約.md)）。
4. 設定を増やしたときは、設定を単一所有する層（`App.Core`）の規則に従って、保存・呼び出し・エクスポート / インポート・プリセットへ通す。
5. 文書は [ドキュメント規約.md](../../ドキュメント規約.md) に従って書く（`README.md` / `SPEC_ExtFunc.md` / `SPEC_System.md`）。
6. `zenhan-verify` の順序で 1 回だけ検証する。

## 一覧を持つ場所（規則は所有者の節を読む）

| 場所 | 所有者 |
| --- | --- |
| `README.md` のプロジェクト構成 | [README.md](../../../README.md) §7 |
| `copilot-instructions.md` の構成表 | [copilot-instructions.md](../../copilot-instructions.md) §3.1 |
| `.vscode/tasks.json` / `.vscode/launch.json` | [検証手順.md](../../検証手順.md) §17 |
| 配布物の一覧（`release-config.json`） | [RELEASE.md](../../RELEASE.md) |

## 報告の形

追加した型と層、4 UI の対応、埋めた一覧、検証の合否と省略した検査。
