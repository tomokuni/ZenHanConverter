using EsUtil.ZenHanConverter.App.Core.Icons;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace EsUtil.ZenHanConverter.App.WinUI3.Helpers;

/// <summary>アプリで使用する Fluent Icons のアイコン形状を提供する静的クラス。</summary>
/// <remarks>提供機能: <br/>
/// - Core の FluentIconData（SVG のパスデータ）から生成した WinUI のアイコン形状の提供<br/><br/>
/// 特徴: <br/>
/// - パスデータは Core が単一所有し、WinUI の形状への変換のみを担う<br/>
/// - MewUI 版（`ZenHanConverter.App.MewUI.Helpers.FluentIcons`）と同じ SVG を共有するため、両 UI のアイコンが一致する<br/>
/// - 形状は取得のたびに生成する（1 つの Geometry を複数の PathIcon へ設定できないため）</remarks>
public static class FluentIcons
{
    /// <summary>Fluent Icons の「Convert Range」アイコン形状を取得します。</summary>
    /// <value>24px グリッドで描かれた Convert Range のベクター形状。</value>
    public static Geometry ConvertRange => ParseGeometry(FluentIconData.ConvertRange);

    /// <summary>Fluent Icons の「Settings」アイコン形状を取得します。</summary>
    /// <value>24px グリッドで描かれた Settings のベクター形状。</value>
    public static Geometry Settings => ParseGeometry(FluentIconData.Settings);

    /// <summary>SVG のパスデータを WinUI の形状へ変換します。</summary>
    /// <param name="pathData">SVG のパスデータ（d 属性の値）。</param>
    /// <returns>変換したベクター形状。</returns>
    /// <remarks>WinUI の Geometry には静的 Parse がないため、XAML の型変換（XamlBindingHelper）を利用します。<br/>
    /// 変換した形状は PathIcon が占有するため、使い回さず要素ごとに生成します
    /// （同じインスタンスを別の PathIcon へ設定すると E_INVALIDARG「Value does not fall within the expected range」で失敗します）。</remarks>
    private static Geometry ParseGeometry(string pathData)
        => (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), pathData);
}
