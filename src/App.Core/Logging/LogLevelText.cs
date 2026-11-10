using System;

namespace EsUtil.ZenHanConverter.App.Core.Logging;

/// <summary>ログのレベルを 3 文字の表記（<c>[TRC]</c> 等）へ写します。</summary>
/// <remarks>
/// ログの行は <c>[TRC]</c> / <c>[DBG]</c> / <c>[INF]</c> / <c>[WRN]</c> / <c>[ERR]</c> / <c>[CRT]</c> の
/// 3 文字でレベルを表します（規則の所有者は <c>.github/ログ規約.md</c> §2）。<br/>
/// Serilog の <c>{Level:u3}</c> は <c>Verbose</c> を <c>VRB</c> と表すため使えません（<c>TRC</c> になりません）。<br/>
/// 写す規則は<b>本クラスが単一所有する</b>（4 つの UI 版が同じ表記になります）。
/// Serilog に依存しません（レベル名の文字列だけを受け取ります）。
/// </remarks>
public static class LogLevelText
{
    /// <summary>ログ イベントのプロパティ名（出力テンプレートの <c>{LogLevelText}</c> が読みます）。</summary>
    public const string PropertyName = "LogLevelText";

    /// <summary>レベル名を 3 文字の表記へ写します。</summary>
    /// <param name="levelName">レベル名（<c>Trace</c> / <c>Debug</c> / <c>Information</c> / <c>Warning</c> / <c>Error</c> / <c>Critical</c>。 Serilog の <c>Verbose</c> / <c>Fatal</c> も受け付けます）。</param>
    /// <returns>3 文字の表記（例: <c>TRC</c>）。 未知の名前は先頭 3 文字を大文字にしたもの。</returns>
    /// <remarks><c>Trace</c> は Serilog では <c>Verbose</c>、<c>Critical</c> は <c>Fatal</c> に対応します（両方を受け付けます）。</remarks>
    public static string Abbreviate(string levelName) => levelName switch
    {
        "Trace" or "Verbose" => "TRC",
        "Debug" => "DBG",
        "Information" => "INF",
        "Warning" => "WRN",
        "Error" => "ERR",
        "Critical" or "Fatal" => "CRT",
        _ => AbbreviateUnknown(levelName),
    };

    /// <summary>未知のレベル名を表記へ写します（先頭 3 文字・大文字）。</summary>
    /// <param name="levelName">レベル名。</param>
    /// <returns>3 文字以内の表記。 空の場合は <c>???</c>。</returns>
    private static string AbbreviateUnknown(string levelName)
        => string.IsNullOrEmpty(levelName)
            ? "???"
            : levelName[..Math.Min(3, levelName.Length)].ToUpperInvariant();
}
