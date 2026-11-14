# アプリ固有 ViewModel のパリティ検証

`src/App.MewUI`・`src/App.WinUI3`・`src/App.WinForms` の**挙動が一致すること**を検証するテストプロジェクトです。

## 1. 目的

共有コア（`src/App.Core`）と設定画面の ViewModel（`src/App.Core.Presentation`）は 4 つの UI で共通です。
一方で `HomeViewModel` は UI スレッドへの委譲方法がフレームワークごとに異なるため、各アプリが個別に実装しています。
本プロジェクトは、同じ入力に対するそれらの出力を直接比較し、共有コアの変更にどれかが追随し忘れる状態を検出します。

## 2. 実行

```powershell
dotnet test --project test\parity\tests_parity.csproj
```

## 3. 構成

| ファイル | 検証内容 |
| --- | --- |
| `HomeViewModelParityTests` | MewUI 版と WinUI 版の表示用変換、クリップボード変更時の書き戻し、変化なし時の扱い、`BeforeText` の追従、全角/半角変換の常時有効 |
| `WinFormsParityTests` | WinForms 版を加えた 3 実装の表示用変換・書き戻し |
| `FakeClipboardService` | 各テストが共有するテスト用クリップボードサービス |

設定画面の ViewModel は共通化済みのため、ここで比較する対象はありません（`test/App.Core.Presentation` が検証します）。

### 3.1 実装方式（ソースリンク）

`App.MewUI` / `App.WinForms` は非自己完結型 exe、`App.WinUI3` は自己完結型 exe のため、1 つのプロジェクトから
これらを同時に `ProjectReference` すると `NETSDK1150` / `NETSDK1151` で失敗します。

そこで **`App.WinUI3` はプロジェクト参照**し、**`App.MewUI` / `App.WinForms` は `HomeViewModel` のソースをリンク**して
同一アセンブリへ取り込んでいます（`tests_parity.csproj` の `Compile Include`）。
リンク先の `HomeViewModel` は共有コアと共有プレゼンテーション層しか参照しないため、この方法で問題なくコンパイルできます。
リンクのため変更は自動的にテストへ反映されます。

> 注意: ソースリンク先のファイルを古いタイムスタンプで上書き（バックアップからの復元など）すると、
> 増分ビルドが変更を検出せず古い内容で実行されることがあります。その場合は `--no-incremental` で再ビルドしてください。
> リンク先のソースは共有プレゼンテーション層の型を `using EsUtil.ZenHanConverter.App.Core.Presentation.ViewModels;` で参照するため、
> 本プロジェクトにも同じ名前空間の参照が必要です。

## 4. 検証済みの一致（自動テストで保証）

- プリセット別の変換結果（全力会計 / 英数記号半角、かな全角 の代表例）
- 変化しない入力・空文字の扱い（`変換不要 (変更なし)` を含む）
- 個別モードの変換（数字の半角/全角）
- 全角/半角変換が常に有効であること（オン/オフの切り替え UI を持たない）
- クリップボード変更時の書き戻し内容
- クリップボード変換トグルが無効な場合に読み書きしないこと

## 5. 意図的な差分（プラットフォーム都合による実装差）

挙動は同じで、実装手段だけが異なる項目です。

| 項目 | MewUI 版 | WinUI 版 | WinForms 版 | Avalonia UI 版 | 備考 |
| --- | --- | --- | --- | --- | --- |
| クリップボード監視方式 | Win32 シーケンス番号のポーリング（`DispatcherTimer`） | WinRT `Clipboard.ContentChanged` イベント | Win32 シーケンス番号のポーリング（`Forms.Timer`） | Win32 シーケンス番号のポーリング（`DispatcherTimer`） | 変更検出は Core の `ClipboardChangeDetector` に共通化済み |
| `ReplacePairItem` の変更通知 | `ObservableObject`（INPC あり） | 素のクラス（INPC なし） | `ObservableObject`（INPC あり） | `ObservableObject`（INPC あり） | `ReplacePairItem` は共通化済みのため、差異は WinUI 版の表示更新手段のみ |
| ウィンドウ位置の補正 | 起動時に Core の `ScreenVisibleArea` で補正 | 同左 | 同左 | 同左 | 補正ロジックは Core で共有 |
| 変換項目の並べ方 | 複数列（記号はマルチカラム） | 折り返し配置 | 1 行に 1 項目 | 折り返し配置 | 項目の並び順は全 UI で同一 |
| アイコンの描画 | MewUI の `PathGeometry` | WinUI の `Geometry` | `SvgPathParser` で `GraphicsPath` へ変換 | Avalonia の `Geometry` | パスデータは Core の `FluentIconData` が単一所有 |
| ホーム画面の上下分割 | MewUI の `SplitPanel`（スターサイズ指定） | `Grid` の行の重み + 自前の `SplitterRegion` | `TableLayoutPanel` の割合 + 自前の分割バー | `Grid` の行の重み + `Thumb` | 比率の換算・上下限は MewUI 版を除き Core の `SplitLayout` を共有 |
| ナビゲーションペインの幅・折りたたみ | `NavigationView` の `PaneWidth` 100（常時表示） | `NavigationView`（`LeftCompact`・`OpenPaneLength` 104） | `ListBox` の幅を 104 ↔ 44 で切替（☰ ボタン） | `ListBox` の幅を 104 ↔ 44 で切替（ペイン開閉ボタン） | 折りたたみ時はラベルを隠しアイコンのみを表示する挙動で統一。開いたときの幅は MewUI 版（100 DIP）に合わせる |
| タイトルバーのプリセット選択の幅 | 項目の文字幅に追従（幅指定なし） | 項目の文字幅に追従（幅指定なし） | `TextRenderer` で項目の文字幅を測定（120〜300 の範囲で補正） | 項目の文字幅に追従（幅指定なし） | 幅は固定値ではなく項目（プリセット名）に応じて変わる |
| 配布方式 | Native AOT（単一バイナリ） | 自己完結・単一ファイル | 自己完結・単一ファイル | Native AOT + SkiaSharp のネイティブ DLL | 各フレームワークの AOT / トリム対応状況による |

> 補足: 全 UI とも、タイトルバーの操作系コントロール（クリップボード変換スイッチ・プリセット選択）は同じ並びで配置しています。
> WinUI の `TitleBar` コントロール（Windows App SDK 1.7+）は `Content` に置いた `ToggleSwitch` / `ComboBox` へ入力が届きます
> （実機検証済み。スイッチのクリックで `AppSetting.IsClipboardConvertEnabled` が変化し、ドロップダウンの選択でプリセットが適用される）。
> `Window.SetTitleBar` を自前の要素に対して使う場合は `InputNonClientPointerSource.SetRegionRects(NonClientRegionKind.Passthrough, ...)` による
> 入力のパススルー指定が必要ですが、`TitleBar` コントロールはこれを内部で処理します。
> WinForms 版は `FormBorderStyle=None` とし、移動はタイトルバーのマウス押下で `WM_NCLBUTTONDOWN`（`HTCAPTION`）を送って
> OS のドラッグ操作を開始し、リサイズはフォームが `Padding` で確保した外周で `WM_NCHITTEST` に応答します。
> 子コントロールが覆う領域には `WM_NCHITTEST` が届かないため、このように役割を分けています。

## 6. 4 UI 版で揃えている挙動

次の機能は 4 UI 版とも同じように動作します（実装は各 UI 版が持ち、設定と判定は共有コアが単一所有します）。

| 機能 | 揃え方 |
| --- | --- |
| クリップボード変換の有効/無効トグル | 4 UI 版ともタイトルバーに置き、`AppSetting.IsClipboardConvertEnabled` を共有する |
| ウィンドウ位置の保存・復元・画面外補正 | 4 UI 版とも DPI 換算して保存し、Core の `ScreenVisibleArea` で補正する |
| 変換前テキストボックスの編集 | 4 UI 版とも編集できる（WinUI 3 は `LineNumberTextBox.IsReadOnly` の既定が `true` のため `IsReadOnly="False"` を明示する） |
| 置換ルールの即時検証 | 4 UI 版とも `ReplacePairItem` の変更通知を `SettingsViewModel` が購読して検証する |
| ホーム画面の上下 2 分割 | 4 UI 版とも上下の比率を保持し、ウィンドウの高さを変えても比率を維持する |
| 全角/半角変換の常時有効化 | 4 UI 版とも変換の有効/無効スイッチを画面に置かない（`IsEnabledZenHan` は画面側で参照しない） |
