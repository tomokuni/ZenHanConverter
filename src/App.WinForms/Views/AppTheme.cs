using System.Drawing;
using System.Windows.Forms;

namespace EsUtil.ZenHanConverter.App.WinForms.Views;

/// <summary>WinForms 画面で共有する配色・寸法・フォントを提供します。</summary>
/// <remarks>提供機能: <br/>
/// - 4 つの UI で共通の配色（アクセント色・境界線色・副次テキスト色など）の一元管理<br/>
/// - 画面で使用する寸法・フォントサイズの一元管理<br/><br/>
/// 特徴: <br/>
/// - 色・寸法をここへ集約し、各画面でのマジックナンバー・マジック文字列を排除<br/>
/// - 他の 3 つの UI（MewUI / WinUI 3 / Avalonia UI）と同じ配色値を用い、見た目を揃える<br/>
/// - 寸法は 96 DPI 基準の論理ピクセルで記述する。DPI による拡大縮小は WinForms の
///   <c>Control.AutoScaleMode</c>（Font）が行うため、画面側で個別に換算しない<br/>
/// - フォントはポイント指定のため、DPI に依存せず物理的な大きさが揃う
/// </remarks>
internal static class AppTheme
{
    /// <summary>タイトルバーの背景色。</summary>
    internal static readonly Color TitleBarBackground = Color.FromArgb(0xF3, 0xF3, 0xF3);

    /// <summary>ナビゲーションペインの背景色。</summary>
    internal static readonly Color NavPaneBackground = Color.FromArgb(0xF6, 0xF8, 0xFA);

    /// <summary>境界線の色。</summary>
    internal static readonly Color BorderColor = Color.FromArgb(0xE1, 0xE1, 0xE1);

    /// <summary>設定画面のツールバーを区切る境界線の色。</summary>
    internal static readonly Color ToolbarBorderColor = Color.FromArgb(0xDD, 0xDD, 0xDD);

    /// <summary>アクセント色（セグメント選択の選択中・ナビゲーションの選択中）。</summary>
    internal static readonly Color AccentColor = Color.FromArgb(0x0F, 0x6C, 0xBD);

    /// <summary>アクセント色の上に重ねる文字色。</summary>
    internal static readonly Color AccentForeColor = Color.White;

    /// <summary>項目ホバー時の背景色（半透明の黒を親の背景へ合成した近似色）。</summary>
    internal static readonly Color HoverBackground = Color.FromArgb(0xE9, 0xEC, 0xEF);

    /// <summary>選択中のナビゲーション項目の背景色。</summary>
    internal static readonly Color NavSelectedBackground = Color.FromArgb(0xE0, 0xE8, 0xF2);

    /// <summary>ウィンドウ操作ボタンのホバー背景色。</summary>
    internal static readonly Color CaptionHoverBackground = Color.FromArgb(0xE0, 0xE0, 0xE0);

    /// <summary>閉じるボタンのホバー背景色。</summary>
    internal static readonly Color CloseHoverBackground = Color.FromArgb(0xC4, 0x2B, 0x1C);

    /// <summary>副次テキスト（説明・補足）の色。</summary>
    internal static readonly Color SecondaryForeColor = Color.FromArgb(0x61, 0x61, 0x61);

    /// <summary>エラーメッセージの色。</summary>
    internal static readonly Color ErrorForeColor = Color.FromArgb(0xC4, 0x2B, 0x1C);

    /// <summary>セグメントボタンの境界線の色。</summary>
    internal static readonly Color SegmentBorderColor = Color.FromArgb(0x22, 0x00, 0x00, 0x00);

    /// <summary>トグルスイッチのオフ時のトラック色。</summary>
    internal static readonly Color SwitchOffTrackColor = Color.FromArgb(0x8A, 0x8A, 0x8A);

    /// <summary>タイトルバーの高さ（論理ピクセル）。</summary>
    internal const int TitleBarHeight = 36;

    /// <summary>タイトルバー左端の余白（論理ピクセル）。</summary>
    internal const int TitleBarPadding = 14;

    /// <summary>タイトルバー内のコントロール間隔（論理ピクセル）。</summary>
    internal const int TitleBarSpacing = 10;

    /// <summary>クリップボード変換スイッチとラベルの間隔（論理ピクセル）。</summary>
    internal const int SwitchLabelSpacing = 2;

    /// <summary>ウィンドウ操作ボタンの幅（論理ピクセル）。</summary>
    internal const int CaptionButtonWidth = 46;

    /// <summary>ウィンドウのリサイズ判定に使用する枠の太さ（論理ピクセル）。</summary>
    internal const int ResizeBorderWidth = 6;

    /// <summary>ナビゲーションペインの幅（展開時、論理ピクセル）。</summary>
    internal const int NavPaneWidth = 104;

    /// <summary>ナビゲーションペインの幅（折りたたみ時、アイコンのみ、論理ピクセル）。</summary>
    internal const int NavPaneCompactWidth = 44;

    /// <summary>ナビゲーションの開閉ボタンの幅（論理ピクセル）。</summary>
    internal const int NavToggleButtonWidth = 36;

    /// <summary>ナビゲーション項目の高さ（論理ピクセル）。</summary>
    internal const int NavItemHeight = 36;

    /// <summary>ナビゲーション項目の左端の余白（論理ピクセル）。</summary>
    internal const int NavItemPadding = 14;

    /// <summary>ナビゲーション項目のアイコンと文字の間隔（論理ピクセル）。</summary>
    internal const int NavIconSpacing = 10;

    /// <summary>ナビゲーション ペインの下端に出す UI 名とバージョンの高さ（論理ピクセル）。</summary>
    internal const int NavFooterHeight = 40;

    /// <summary>ナビゲーション ペインの下端に出す UI 名とバージョンの文字色。</summary>
    internal static readonly Color NavFooterForeground = Color.FromArgb(0x60, 0x60, 0x60);

    /// <summary>トグルスイッチのトラックの幅（論理ピクセル）。</summary>
    internal const int SwitchTrackWidth = 40;

    /// <summary>トグルスイッチのトラックの高さ（論理ピクセル）。</summary>
    internal const int SwitchTrackHeight = 20;

    /// <summary>トグルスイッチのノブとトラックの余白（論理ピクセル）。</summary>
    internal const int SwitchKnobInset = 3;

    /// <summary>通常の文字サイズ（pt）。</summary>
    internal const float BodyFontSize = 9f;

    /// <summary>画面見出しの文字サイズ（pt）。</summary>
    internal const float TitleFontSize = 15f;

    /// <summary>カテゴリ見出しの文字サイズ（pt）。</summary>
    internal const float SectionHeaderFontSize = 11.25f;

    /// <summary>説明・補足の文字サイズ（pt）。</summary>
    internal const float DescriptionFontSize = 9f;

    /// <summary>列見出しの文字サイズ（pt）。</summary>
    internal const float ColumnHeaderFontSize = 9f;

    /// <summary>エラーメッセージの文字サイズ（pt）。</summary>
    internal const float ErrorFontSize = 8.25f;

    /// <summary>システムの標準フォントファミリ。</summary>
    /// <remarks>特定のフォント名を前提にせず、環境の標準フォントを基準にサイズと太さのみを変えます。</remarks>
    private static FontFamily BaseFontFamily => SystemFonts.MessageBoxFont?.FontFamily ?? Control.DefaultFont.FontFamily;

    /// <summary>画面見出し用のフォントを作成します。</summary>
    /// <returns>太字の見出しフォント。</returns>
    internal static Font CreateTitleFont() => new(BaseFontFamily, TitleFontSize, FontStyle.Bold);

    /// <summary>カテゴリ見出し用のフォントを作成します。</summary>
    /// <returns>太字のカテゴリ見出しフォント。</returns>
    internal static Font CreateSectionHeaderFont() => new(BaseFontFamily, SectionHeaderFontSize, FontStyle.Bold);

    /// <summary>ラベル用のフォントを作成します。</summary>
    /// <returns>通常の太さのラベルフォント。</returns>
    internal static Font CreateLabelFont() => new(BaseFontFamily, BodyFontSize);

    /// <summary>列見出し用のフォントを作成します。</summary>
    /// <returns>太字の列見出しフォント。</returns>
    internal static Font CreateColumnHeaderFont() => new(BaseFontFamily, ColumnHeaderFontSize, FontStyle.Bold);

    /// <summary>説明・補足用のフォントを作成します。</summary>
    /// <returns>通常の太さの説明フォント。</returns>
    internal static Font CreateDescriptionFont() => new(BaseFontFamily, DescriptionFontSize);

    /// <summary>エラーメッセージ用のフォントを作成します。</summary>
    /// <returns>通常の太さのエラー表示フォント。</returns>
    internal static Font CreateErrorFont() => new(BaseFontFamily, ErrorFontSize);
}
