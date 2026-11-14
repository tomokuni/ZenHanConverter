using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace EsUtil.ZenHanConverter.App.Core.Helpers;

/// <summary>アプリのバージョン表記（ナビゲーション パネルのフッターなどに表示する文字列）と、使っている EsUtil の版を取得します。</summary>
/// <remarks>
/// <b>版の出所</b>: アプリの版は <c>Directory.Build.props</c> の <c>&lt;Version&gt;</c>（全プロジェクト共通の単一所有者）であり、
/// 実行時はアセンブリのバージョンから読みます。 <c>0.5.0.0</c> のように末尾が 0 の桁は表示しません（<c>0.5.0</c>）。<br/>
/// <b>EsUtil の版</b>: 同じ製品バージョン（<c>0.5.0+esutil.0.6.5</c>）へ埋め込まれます。 EsUtil はパッケージ参照であり、
/// ログだけを見ても版が分からないため、起動ログへ残します。<br/>
/// <b>UI 名を含める理由</b>: 4 つの UI 版は設定を共有するため、どの版を起動しているかを画面から判別できるようにします。
/// </remarks>
public static class AppVersion
{
    /// <summary>バージョン番号の前に付ける接頭辞。</summary>
    public const string VersionPrefix = "v";

    /// <summary>版を取得できなかった場合の表記。</summary>
    public const string Unknown = "unknown";

    /// <summary>製品バージョンで EsUtil の版を始める印。</summary>
    private const string EsUtilMark = "+esutil.";

    /// <summary>SourceLink が製品バージョンの末尾へ足すコミット SHA の長さ。</summary>
    private const int SourceRevisionLength = 40;

    /// <summary>EsUtil の版（起動時に 1 度だけ読む）。</summary>
    private static readonly string s_esUtilVersion = ResolveEsUtilVersion();

    /// <summary>アプリのバージョンを取得します（例: <c>0.5.0</c>）。</summary>
    /// <value>バージョン文字列。 取得できない場合は <c>unknown</c>。</value>
    public static string Current => Format(typeof(AppVersion).Assembly);

    /// <summary>本アプリが使っている EsUtil の版を取得します（例: <c>0.6.5</c>）。</summary>
    /// <value>EsUtil の版。 製品バージョンに埋め込まれていない場合は <c>unknown</c>。</value>
    public static string EsUtilVersion => s_esUtilVersion;

    /// <summary>UI 名とバージョンを 2 行で並べた表示用の文字列を取得します（例: <c>AvaloniaUI</c> / <c>v0.5.0</c>）。</summary>
    /// <param name="uiName">UI の名前（<c>AvaloniaUI</c> / <c>MewUI</c> / <c>WinUI3</c> / <c>WinForms</c>）。</param>
    /// <returns>UI 名・改行・<c>v</c> 付きのバージョンを並べた文字列。</returns>
    /// <remarks>
    /// 表記（行の分け方・<see cref="VersionPrefix"/>・改行）は本メソッドが単一所有します
    /// （UI ごとに書式がずれないようにする）。 UI 名が空の場合はバージョンだけを返します。
    /// </remarks>
    public static string For(string uiName)
        => string.IsNullOrWhiteSpace(uiName)
            ? VersionPrefix + Current
            : string.Create(CultureInfo.InvariantCulture, $"{uiName}{Environment.NewLine}{VersionPrefix}{Current}");

    /// <summary>アセンブリのバージョンを表示用の文字列へ整形します。</summary>
    /// <param name="assembly">対象のアセンブリ。</param>
    /// <returns>整形したバージョン文字列。</returns>
    /// <remarks>4 桁のうち末尾の 0 を取り除きます（<c>0.5.0.0</c> → <c>0.5.0</c>）。 すべて 0 になった場合は 1 桁だけ残します。</remarks>
    private static string Format(Assembly assembly)
    {
        if (assembly.GetName().Version is not { } version)
        {
            return Unknown;
        }

        var parts = new List<int> { version.Major, version.Minor, version.Build, version.Revision };
        while (parts.Count > 3 && parts[^1] == 0)
        {
            parts.RemoveAt(parts.Count - 1);
        }

        return string.Join('.', parts);
    }

    /// <summary>製品バージョンから EsUtil の版を読み取ります。</summary>
    /// <returns>EsUtil の版。 製品バージョンに埋め込まれていない場合は <see cref="Unknown"/>。</returns>
    private static string ResolveEsUtilVersion()
    {
        var informational = typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (informational is null)
        {
            return Unknown;
        }

        var index = informational.IndexOf(EsUtilMark, StringComparison.Ordinal);
        return index < 0
            ? Unknown
            : RemoveSourceRevision(informational[(index + EsUtilMark.Length)..]);
    }

    /// <summary>版の文字列の末尾から SourceLink のコミット SHA を取り除きます。</summary>
    /// <param name="value">版の文字列（例: <c>0.6.5.&lt;sha&gt;</c>）。</param>
    /// <returns>コミット SHA を取り除いた版の文字列。</returns>
    /// <remarks>版の一部（<c>-preview.1</c> など）と取り違えないよう、末尾が 16 進 40 桁のときだけ取り除きます。</remarks>
    private static string RemoveSourceRevision(string value)
    {
        var separator = value.LastIndexOf('.', StringComparison.Ordinal);
        if (separator <= 0 || value.Length - separator - 1 != SourceRevisionLength)
        {
            return value;
        }

        foreach (var character in value.AsSpan(separator + 1))
        {
            if (!char.IsAsciiHexDigit(character))
            {
                return value;
            }
        }

        return value[..separator];
    }
}
