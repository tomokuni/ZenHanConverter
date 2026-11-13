using System;
using System.IO;
using System.Text;

namespace EsUtil.ZenHanConverter.App.Core.Icons;

/// <summary>Fluent Icons の SVG アセットからパスデータを提供する静的クラス。</summary>
/// <remarks>提供機能: <br/>
/// - Icons フォルダの SVG アセットから抽出したパスデータ（d 属性の値）の提供<br/><br/>
/// 特徴: <br/>
/// - SVG ファイルをアイコン形状の唯一の定義元とし、パスデータの二重管理を排除<br/>
/// - UI フレームワークに依存しないため、MewUI / WinUI3 の双方で同じアイコン形状を利用可能<br/>
/// - 読み込みは初回アクセスの一度だけ行い、以降は抽出済みの文字列を返却</remarks>
public static class FluentIconData
{
    /// <summary>Convert Range アイコンの SVG ファイル名。</summary>
    private const string ConvertRangeFile = "ic_fluent_convert_range_24_regular.svg";

    /// <summary>Settings アイコンの SVG ファイル名。</summary>
    private const string SettingsFile = "ic_fluent_settings_24_regular.svg";

    /// <summary>SVG のパスデータ属性の先頭。</summary>
    private const string PathDataAttribute = "d=\"";

    /// <summary>埋め込みリソース名の一覧。</summary>
    private static readonly string[] s_resourceNames = typeof(FluentIconData).Assembly.GetManifestResourceNames();

    /// <summary>Convert Range アイコンのパスデータ（初回アクセス時に抽出）。</summary>
    private static string? s_convertRange;

    /// <summary>Settings アイコンのパスデータ（初回アクセス時に抽出）。</summary>
    private static string? s_settings;

    /// <summary>Fluent Icons の「Convert Range」アイコンの SVG パスデータを取得します。</summary>
    /// <value>24px グリッドで描かれた Convert Range の SVG パスデータ（d 属性の値）。</value>
    public static string ConvertRange => s_convertRange ??= Load(ConvertRangeFile);

    /// <summary>Fluent Icons の「Settings」アイコンの SVG パスデータを取得します。</summary>
    /// <value>24px グリッドで描かれた Settings の SVG パスデータ（d 属性の値）。</value>
    public static string Settings => s_settings ??= Load(SettingsFile);

    /// <summary>指定された SVG ファイルからパスデータを読み込みます。</summary>
    /// <param name="fileName">Icons フォルダ内の SVG ファイル名。</param>
    /// <returns>抽出したパスデータ。</returns>
    /// <exception cref="InvalidOperationException">埋め込みリソースまたはパスデータが見つからない場合。</exception>
    private static string Load(string fileName)
    {
        var resourceName = FindResourceName(fileName);
        using var stream = typeof(FluentIconData).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"アイコンリソースを開けません: {resourceName}");

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return ExtractPathData(reader.ReadToEnd());
    }

    /// <summary>埋め込みリソース名から SVG ファイル名に一致するものを検索します。</summary>
    /// <param name="fileName">Icons フォルダ内の SVG ファイル名。</param>
    /// <returns>一致した埋め込みリソース名。</returns>
    /// <exception cref="InvalidOperationException">一致する埋め込みリソースが存在しない場合。</exception>
    private static string FindResourceName(string fileName)
    {
        foreach (var name in s_resourceNames)
        {
            if (name.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
                return name;
        }

        throw new InvalidOperationException($"アイコンリソースが見つかりません: {fileName}");
    }

    /// <summary>SVG の内容からパスデータ（d 属性の値）を抽出します。</summary>
    /// <param name="svg">SVG ファイルの内容。</param>
    /// <returns>抽出したパスデータ。</returns>
    /// <exception cref="InvalidOperationException">パスデータ属性が存在しない、または閉じられていない場合。</exception>
    private static string ExtractPathData(string svg)
    {
        var start = svg.IndexOf(PathDataAttribute, StringComparison.Ordinal);
        if (start < 0)
            throw new InvalidOperationException("SVG にパスデータ（d 属性）がありません。");

        start += PathDataAttribute.Length;
        var end = svg.IndexOf('"', start, StringComparison.Ordinal);
        if (end < 0)
            throw new InvalidOperationException("SVG のパスデータ（d 属性）が閉じられていません。");

        return svg[start..end];
    }
}
