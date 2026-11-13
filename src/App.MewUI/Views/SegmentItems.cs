using Aprillz.MewUI;
using Aprillz.MewUI.Text;
using EsUtil.ZenHanConverter.App.Core.Presentation.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EsUtil.ZenHanConverter.App.MewUI.Views;

/// <summary>設定画面のセグメントコントロール構築とバインディングを支援するヘルパークラス。</summary>
/// <remarks>
/// ZenHanConvertItem の選択肢（Options）を MewUI の SegmentedControl に接続し、<br/>
/// 選択変更を ConvertConfig の対応プロパティへ書き戻すための変換ロジックと、<br/>
/// テキスト幅の計測（ラベル・セグメント）を行うヘルパーを提供します。<br/>
/// テキスト計測は Application.DefaultGraphicsFactory を利用するため、<br/>
/// 呼び出し時点で MewUI アプリケーションが起動済みである必要があります。
/// </remarks>
internal static class SegmentItems
{
    /// <summary>選択肢一覧から SegmentedControl 用の ItemsSource を生成します。</summary>
    /// <param name="options">表示する選択肢一覧。</param>
    /// <returns>表示ラベル文字列の ItemsView。</returns>
    public static ISelectableItemsView Create(IReadOnlyList<SegmentOption> options)
        => ItemsView.Create([.. options.Select(s => s.Content)]);

    /// <summary>現在の設定値に対応するセグメントのインデックスを取得します。</summary>
    /// <param name="item">変換項目。</param>
    /// <returns>現在値に対応するセグメントインデックス。</returns>
    public static int GetSelectedIndex(ZenHanConvertItem item)
    {
        for (var i = 0; i < item.Options.Count; i++)
        {
            if (Equals(item.Options[i].Content, item.SelectedLabel))
                return i;
        }
        return 0;
    }

    /// <summary>指定されたテキストの描画幅を計測します。</summary>
    /// <param name="text">計測するテキスト。</param>
    /// <returns>テキストの描画幅（ピクセル）。</returns>
    /// <remarks>既定テーマのフォント（ThemeMetrics.Default）とDPI96 を基準に計測します。<br/>
    /// ITextEngine のレイアウトキャッシュを使用して AOT 互換で計測します。<br/>
    /// グループ内での最大幅比較が目的のため、絶対精度は問いません。</remarks>
    public static double MeasureTextWidth(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var request = new TextLayoutRequest
        {
            Text = text.AsMemory(),
            Dpi = 96,
            DefaultStyle = TextRunStyle.Default,
        };

        var layout = Application.DefaultGraphicsFactory.TextEngine
            .GetOrCreateLayout(request, TextLayoutCachePolicy.None, null);

        return layout.MeasuredSize.Width;
    }

    /// <summary>項目ラベルの名称部と記号部を分ける区切り文字（全角スペース）。</summary>
    private const char LabelSeparator = '\u3000';

    /// <summary>項目ラベルを名称部と記号部に分割します。</summary>
    /// <param name="label">項目ラベル（"名称　記号" 形式。記号がないラベルも許容）。</param>
    /// <returns>名称部と記号部（記号がない場合は記号部が空文字）。</returns>
    /// <remarks>区切りは最後の全角スペースとし、名称に含まれる空白はそのまま名称部へ残します。</remarks>
    public static (string Name, string Symbol) SplitLabel(string label)
    {
        var index = label.LastIndexOf(LabelSeparator, StringComparison.Ordinal);
        return index < 0 ? (label, string.Empty) : (label[..index], label[(index + 1)..]);
    }

    /// <summary>グループ内の名称部の最大描画幅を取得します。</summary>
    /// <param name="items">グループ内の変換項目。</param>
    /// <returns>名称部の幅の最大値（0 の場合は計測不可）。</returns>
    public static double GetMaxNameWidth(IEnumerable<ZenHanConvertItem> items)
        => items.Select(i => MeasureTextWidth(SplitLabel(i.Label).Name)).DefaultIfEmpty(0).Max();

    /// <summary>グループ内の記号部の最大描画幅を取得します。</summary>
    /// <param name="items">グループ内の変換項目。</param>
    /// <returns>記号部の幅の最大値（記号がない場合は 0）。</returns>
    public static double GetMaxSymbolWidth(IEnumerable<ZenHanConvertItem> items)
        => items.Select(i => MeasureTextWidth(SplitLabel(i.Label).Symbol)).DefaultIfEmpty(0).Max();

    /// <summary>グループ内の各セグメントテキストの最大描画幅を取得します。</summary>
    /// <param name="items">グループ内の変換項目。</param>
    /// <returns>セグメントテキスト幅の最大値（0 の場合は計測不可）。</returns>
    public static double GetMaxSegmentTextWidth(IEnumerable<ZenHanConvertItem> items)
        => items.SelectMany(i => i.Options)
            .Select(s => MeasureTextWidth(s.Content))
            .DefaultIfEmpty(0).Max();
}
