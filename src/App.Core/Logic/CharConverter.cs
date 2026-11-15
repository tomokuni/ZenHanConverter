using EsUtil.ZenHanConverter.App.Core.Enums;
using EsUtil.ZenHanConverter.App.Core.Helpers;
using EsUtil.ZenHanConverter.App.Core.Interfaces;
using EsUtil.ZenHanConverter.App.Core.Models;
using EsUtil.Feature.ZenHan;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.RegularExpressions;
using static EsUtil.Feature.ZenHan.Define;
// 名前空間のルート（EsUtil.ZenHanConverter）が型 ZenHanConverter と同じ名前になるため、
// `ZenHanConverter.ToNormalize` が名前空間として解決される。 別名で型を指す。
using ZenHanApi = EsUtil.Feature.ZenHan.ZenHanConverter;

namespace EsUtil.ZenHanConverter.App.Core.Logic;

/// <summary>ConvertConfig の設定に基づいて文字列の全角/半角変換を実行します。</summary>
/// <remarks>
/// 提供機能: <br/>
/// - 数字・英字の全角/半角変換<br/>
/// - 記号類（括弧・引用符・演算子等）の全角/半角変換<br/>
/// - かな文字（半角カナ、全角カタカナ、全角ひらがな）の相互変換<br/>
/// - 円記号/バックスラッシュの変換<br/>
/// - タブ/改行/連続スペースの整形<br/>
/// - ユーザー定義の置換ルール適用<br/><br/>
/// 特徴: <br/>
/// - EsUtil.Feature.ZenHan.Core ライブラリに基づく正確な変換ペア<br/>
/// - Config 変更時に変換ペアのキャッシュを破棄する<br/><br/>
/// 注意点: <br/>
/// - 変換は文字単位のペアテーブル置換に基づく<br/>
/// - ユーザー定義置換は正規表現エラー時も無視（処理継続）</remarks>
public partial class CharConverter : ITextConverter, IDisposable
{
    /// <summary>変換設定を取得します。</summary>
    /// <value>このコンバーターが使用する ConvertConfig インスタンス。</value>
    public ConvertConfig Config { get; private set; }

    /// <summary>キャッシュされた変換ペア。Config 変更時に null にリセットされます。</summary>
    private ConvertPairs? _cachedPairs;
    /// <summary>Dispose 済みフラグ。</summary>
    private bool _disposed;

    // ─── 単一のシンボルマップ定義 ───

    /// <summary>モード取得デリゲートと EsUtil エントリのペア。</summary>
    /// <param name="GetMode">ConvertConfig からモード値を取得するデリゲート</param>
    /// <param name="Entry">EsUtil の変換エントリ（IZenHanConverterToHanToZen または特殊オブジェクト）</param>
    /// <param name="IsPair">true の場合 Entry は IZenHanConverterToHanToZen[]（左右ペア）</param>
    private sealed record MapEntry(
        Func<ConvertConfig, Enum> GetMode,
        object Entry,
        bool IsPair = false);

    /// <summary>全ての記号マップエントリを宣言的に定義します。</summary>
    /// <remarks>各エントリは ConvertConfig のモードプロパティと EsUtil の変換エントリを対応付けます。<br/>
    /// IsPair=true のエントリは左右のペア（例: （ と ））を同時に処理します。</remarks>
    private static readonly MapEntry[] SymbolMap =
    [
        // ─── 数字・英字 ───
        new(c => c.ConvertModeNumber, GroupOf.Ascii.Numeric),
        new(c => c.ConvertModeAlphabet, GroupOf.Ascii.Alphabet),

        // ─── Ascii 記号（ペアは IsPair=true） ───
        new(c => c.ConvertModeSymbolParenthesis, new IZenHanConverterToHanToZen[]
            { NameOf.Ascii.ParenthesisLeft, NameOf.Ascii.ParenthesisRight }, IsPair: true),
        new(c => c.ConvertModeSymbolSquareBracket, new IZenHanConverterToHanToZen[]
            { NameOf.Ascii.SquareBracketLeft, NameOf.Ascii.SquareBracketRight }, IsPair: true),
        new(c => c.ConvertModeSymbolCurlyBracket, new IZenHanConverterToHanToZen[]
            { NameOf.Ascii.CurlyBracketLeft, NameOf.Ascii.CurlyBracketRight }, IsPair: true),
        new(c => c.ConvertModeSymbolDoubleQuote, NameOf.Ascii.DoubleQuote),
        new(c => c.ConvertModeSymbolSingleQuote, NameOf.Ascii.SingleQuote),
        new(c => c.ConvertModeSymbolComma, NameOf.Ascii.Comma),
        new(c => c.ConvertModeSymbolPeriod, NameOf.Ascii.Period),
        new(c => c.ConvertModeSymbolColon, NameOf.Ascii.Colon),
        new(c => c.ConvertModeSymbolSemicolon, NameOf.Ascii.Semicolon),
        new(c => c.ConvertModeSymbolLessThan, NameOf.Ascii.LessThan),
        new(c => c.ConvertModeSymbolEqual, NameOf.Ascii.Equal),
        new(c => c.ConvertModeSymbolGreaterThan, NameOf.Ascii.GreaterThan),
        new(c => c.ConvertModeSymbolPlus, NameOf.Ascii.Plus),
        new(c => c.ConvertModeSymbolHyphenMinus, NameOf.Ascii.HyphenMinus),
        new(c => c.ConvertModeSymbolExclamation, NameOf.Ascii.Exclamation),
        new(c => c.ConvertModeSymbolSharp, NameOf.Ascii.Sharp),
        new(c => c.ConvertModeSymbolDollar, NameOf.Ascii.Dollar),
        new(c => c.ConvertModeSymbolPercent, NameOf.Ascii.Percent),
        new(c => c.ConvertModeSymbolAmpersand, NameOf.Ascii.Ampersand),
        new(c => c.ConvertModeSymbolAsterisk, NameOf.Ascii.Asterisk),
        new(c => c.ConvertModeSymbolSlash, NameOf.Ascii.Slash),
        new(c => c.ConvertModeSymbolQuestion, NameOf.Ascii.Question),
        new(c => c.ConvertModeSymbolAt, NameOf.Ascii.At),
        new(c => c.ConvertModeSymbolCaret, NameOf.Ascii.Caret),
        new(c => c.ConvertModeSymbolUnderBar, NameOf.Ascii.UnderBar),
        new(c => c.ConvertModeSymbolBackquote, NameOf.Ascii.Backquote),
        new(c => c.ConvertModeSymbolVerticalBar, NameOf.Ascii.VerticalBar),
        new(c => c.ConvertModeSymbolTilde, NameOf.Ascii.Tilde),
        new(c => c.ConvertModeSymbolSpace, NameOf.Ascii.Space),

        // ─── かな記号 ───
        new(c => c.ConvertModeEtcKanaVoice, NameOf.Kana.Voice),
        new(c => c.ConvertModeEtcKanaSemiVoice, NameOf.Kana.SemiVoice),
        new(c => c.ConvertModeEtcKanaMiddleDot, NameOf.Kana.MiddleDot),
        new(c => c.ConvertModeEtcKanaLeftCornerBracket, NameOf.Kana.LeftCornerBracket),
        new(c => c.ConvertModeEtcKanaRightCornerBracket, NameOf.Kana.RightCornerBracket),

        // ─── かな約物（長音/読点/句点） ───
        new(c => c.ConvertModeEtcKanaProlong, NameOf.Kana.Prolong),
        new(c => c.ConvertModeEtcKanaPeriod, NameOf.Kana.Period),
        new(c => c.ConvertModeEtcKanaComma, NameOf.Kana.Comma),
    ];

    /// <summary>CharConverter の新しいインスタンスを初期化します。</summary>
    /// <param name="config">変換設定。この設定の変更を監視し、キャッシュを自動無効化します。</param>
    public CharConverter(ConvertConfig config)
    {
        Config = config;
        Config.PropertyChanged += OnConfigPropertyChanged;
    }

    /// <summary>設定変更時にキャッシュを無効化します。</summary>
    private void OnConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => _cachedPairs = null;

    /// <summary>リソースを解放します。Config.PropertyChanged の購読を解除します。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        Config.PropertyChanged -= OnConfigPropertyChanged;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>モード種別と EsUtil エントリから変換ペアを解決します。</summary>
    /// <param name="mode">変換モード（列挙型）。ZenHanMode / ZenHanKanaMode / ZenHanEtcZenHanAsciiMode のいずれか。</param>
    /// <param name="entry">EsUtil の変換エントリオブジェクト。</param>
    /// <returns>解決された変換ペア。該当なしの場合は ConvertPairs.Empty。</returns>
    static ConvertPairs ResolvePairs(Enum mode, object entry)
        => mode switch
        {
            ZenHanMode m when entry is IZenHanConverterToHanToZen e => m.GetConvertPairs(e),
            ZenHanKanaMode m when entry is IZenHanConverterToHanToZen e => m.GetConvertPairs(e),
            ZenHanEtcZenHanAsciiMode m => m.GetConvertPairs(entry),
            _ => ConvertPairs.Empty,
        };

    /// <summary>円記号/バックスラッシュ変換モードに応じた変換ペアを生成します。</summary>
    /// <param name="mode">変換モード。</param>
    /// <param name="src">変換元の文字（"\", "＼", "¥", "￥"）。</param>
    /// <returns>対応する1件の変換ペア。該当なしの場合は ConvertPairs.Empty。</returns>
    static ConvertPairs GetYenConvertPairs(ZenHanEtcYenMode mode, string src)
        => mode switch
        {
            ZenHanEtcYenMode.ToHanBSlash => new ConvertPairs([(src, "\\")]),
            ZenHanEtcYenMode.ToZenBSlash => new ConvertPairs([(src, "＼")]),
            ZenHanEtcYenMode.ToHanYen => new ConvertPairs([(src, "¥")]),
            ZenHanEtcYenMode.ToZenYen => new ConvertPairs([(src, "￥")]),
            _ => ConvertPairs.Empty,
        };

    /// <summary>現在の Config に基づいて全ての変換ペアを生成します。</summary>
    /// <remarks>結果はキャッシュされ、Config が変更されるまで再利用されます。<br/>
    /// 変換ペアの生成順序: <br/>
    /// 1. SymbolMap のループ処理（記号類）<br/>
    /// 2. かな変換（半角/全角カタカナ/全角ひらがな）<br/>
    /// 3. 円記号/バックスラッシュ変換<br/>
    /// 4. タブ/改行変換<br/>
    /// 5. 単一の ConvertPairs に統合</remarks>
    /// <returns>現在の設定に基づく変換ペア</returns>
    public ConvertPairs GetConvertPairs()
    {
        if (_cachedPairs is not null)
            return _cachedPairs;

        List<(string From, string To)> pairs = [];

        // 1. SymbolMap のループ処理
        foreach (var entry in SymbolMap)
        {
            var mode = entry.GetMode(Config);

            if (entry.IsPair && entry.Entry is IZenHanConverterToHanToZen[] pairEntries)
            {
                foreach (var pe in pairEntries)
                    pairs.AddRange(ResolvePairs(mode, pe));
            }
            else
            {
                pairs.AddRange(ResolvePairs(mode, entry.Entry));
            }
        }

        // 2. かな変換
        pairs.AddRange(Config.ConvertModeKanaHan switch
        {
            ZenHanKanaMode.ToZenKata => GroupOf.Kana.Kata.ToZenMap,
            ZenHanKanaMode.ToZenHira => GroupOf.Kana.Hira.ToZenMap,
            _ => ConvertPairs.Empty,
        });
        pairs.AddRange(Config.ConvertModeKanaZenKata switch
        {
            ZenHanKanaMode.ToHan => GroupOf.Kana.Kata.ToHanMap,
            ZenHanKanaMode.ToZenHira => GroupOf.Kana.ToHiraMap,
            _ => ConvertPairs.Empty,
        });
        pairs.AddRange(Config.ConvertModeKanaZenHira switch
        {
            ZenHanKanaMode.ToHan => GroupOf.Kana.Hira.ToHanMap,
            ZenHanKanaMode.ToZenHira => GroupOf.Kana.ToKataMap,
            _ => ConvertPairs.Empty,
        });

        // 3. 円記号/バックスラッシュ変換
        pairs.AddRange(GetYenConvertPairs(Config.ConvertModeEtcBSlashHan, "\\"));
        pairs.AddRange(GetYenConvertPairs(Config.ConvertModeEtcBSlashZen, "＼"));
        pairs.AddRange(GetYenConvertPairs(Config.ConvertModeEtcYenHan, "¥"));
        pairs.AddRange(GetYenConvertPairs(Config.ConvertModeEtcYenZen, "￥"));

        // 4. タブ/改行変換
        pairs.AddRange(Config.ConvertModeEtcTabSpace switch
        {
            ZenHanEtcSpecial.ToHanSpace => new ConvertPairs([("\t", " ")]),
            ZenHanEtcSpecial.ToZenSpace => new ConvertPairs([("\t", "　")]),
            ZenHanEtcSpecial.Remove => new ConvertPairs([("\t", "")]),
            _ => ConvertPairs.Empty,
        });
        pairs.AddRange(Config.ConvertModeEtcNewline switch
        {
            ZenHanEtcSpecial.ToHanSpace => new ConvertPairs([("\r\n", " "), ("\n", " "), ("\r", " ")]),
            ZenHanEtcSpecial.ToZenSpace => new ConvertPairs([("\r\n", "　"), ("\n", "　"), ("\r", "　")]),
            ZenHanEtcSpecial.Remove => new ConvertPairs([("\r\n", ""), ("\n", ""), ("\r", "")]),
            _ => ConvertPairs.Empty,
        });

        _cachedPairs = new ConvertPairs([.. pairs]);
        return _cachedPairs;
    }

    /// <summary>テキスト変換を実行します。</summary>
    /// <remarks>
    /// 処理フロー: <br/>
    /// 1. null/空文字チェック（そのまま返す）<br/>
    /// 2. 全角/半角変換<br/>
    ///    2a. ZenHanApi.ToNormalize で正規化<br/>
    ///    2b. 変換ペアに基づく置換<br/>
    ///    2c. 連続スペースの整形<br/>
    /// 3. ユーザー定義の置換ルール適用<br/><br/>
    /// 注意点: <br/>
    /// - 全角/半角変換は常に有効（呼び出し側でスキップしない）<br/>
    /// - ユーザー定義の置換ルールは全角/半角変換に続けて常に適用される</remarks>
    /// <param name="text">変換対象のテキスト。null の場合は null を返す。</param>
    /// <returns>変換結果のテキスト</returns>
    public string Convert(string text)
    {
        // Step 1: null/空文字チェック
        if (string.IsNullOrEmpty(text))
            return text;

        // Step 2: 全角/半角変換
        text = ZenHanApi.ToNormalize(text);
        text = GetConvertPairs().Convert(text);
        text = Config.ConvertModeEtcMultiSpace switch
        {
            ZenHanEtcSpecial.ToHanSpace => SingleSpaceRegex().Replace(text, " "),
            ZenHanEtcSpecial.ToZenSpace => SingleSpaceRegex().Replace(text, "　"),
            ZenHanEtcSpecial.Remove => SingleSpaceRegex().Replace(text, ""),
            _ => text
        };

        // Step 3: ユーザー定義の置換ルール適用
        return ApplyUserReplacements(text);
    }

    /// <summary>ユーザー定義の置換ルールを適用します。</summary>
    /// <param name="text">置換対象のテキスト</param>
    /// <returns>置換結果のテキスト</returns>
    /// <remarks>正規表現エラーは無視して処理を続行します。<br/>
    /// 空の検索文字列を持つルールはスキップされます。<br/>
    /// 置換は定義順に逐次適用されます。<br/>
    /// 正規表現の規則は、パターンごとに組み立てた <see cref="Regex"/> を使い回します。
    /// </remarks>
    private string ApplyUserReplacements(string text)
    {
        foreach (var pair in Config.ReplacePairs)
        {
            if (string.IsNullOrEmpty(pair.Search))
                continue;

            if (!pair.IsRegex)
            {
                text = text.Replace(pair.Search, pair.Replace, StringComparison.Ordinal);
                continue;
            }

            // 不正なパターンは初回に分かり、以降も「置換しない」で安定する（毎回例外を投げ直さない）
            var regex = GetOrAddRegex(pair.Search);
            if (regex is not null)
                text = regex.Replace(text, pair.Replace);
        }
        return text;
    }

    /// <summary>正規表現のキャッシュ（パターン → 組み立て済みの <see cref="Regex"/>。 不正なパターンは null）。</summary>
    /// <remarks>同じ設定なら同じ結果になる。 上限を超えたら捨てる。</remarks>
    private readonly Dictionary<string, Regex?> _regexCache = new(StringComparer.Ordinal);

    /// <summary>正規表現のキャッシュの上限。</summary>
    private const int MaxCachedRegexes = 256;

    /// <summary>パターンに対応する <see cref="Regex"/> を取得します（無ければ組み立てます）。</summary>
    /// <param name="pattern">正規表現のパターン。</param>
    /// <returns>組み立てた正規表現。 パターンが不正な場合は null。</returns>
    private Regex? GetOrAddRegex(string pattern)
    {
        if (_regexCache.TryGetValue(pattern, out var cached))
            return cached;

        Regex? regex = null;
        try
        {
            // タイムアウトは RegexSettings が単一所有する（壊れたパターンで固まらないようにする）
            regex = new Regex(pattern, RegexOptions.None, RegexSettings.MatchTimeout);
        }
        catch (ArgumentException)
        {
            // ユーザー入力の正規表現が不正な場合も処理を続行
        }

        if (_regexCache.Count >= MaxCachedRegexes)
            _regexCache.Clear();

        _regexCache[pattern] = regex;
        return regex;
    }

    /// <summary>連続するスペース（半角/全角混在）を検出する正規表現。</summary>
    [GeneratedRegex("[ 　]{2,}", RegexOptions.None, 1000)]
    private static partial Regex SingleSpaceRegex();
}
