using EsUtil.ZenHanConverter.App.WinForms.Helpers;
using EsUtil.ZenHanConverter.App.Core.Icons;
using System;
using System.Drawing.Drawing2D;
using Xunit;

namespace EsUtil.ZenHanConverter.Tests.App.WinForms.Helpers;

/// <summary><see cref="SvgPathParser"/> の SVG パス解釈と GDI+ 図形パスへの変換を検証します。</summary>
/// <remarks>Fluent Icons のアイコンが実際に解釈できることを保証するため、Core が所有するパスデータも対象にします。</remarks>
public sealed class SvgPathParserTests
{
    /// <summary>検証対象のアイコンのパスデータ。</summary>
    /// <value>Core が所有する 2 つの Fluent Icons のパスデータ。</value>
    /// <remarks>属性引数には実行時の値を使用できないため、<see cref="MemberDataAttribute"/> で供給します。</remarks>
    public static TheoryData<string> IconPathData { get; } = new() { FluentIconData.ConvertRange, FluentIconData.Settings };

    /// <summary>Core が所有する 2 つのアイコンが例外なく解釈でき、図形を持つことを検証します。</summary>
    /// <param name="pathData">検証する SVG パスデータ。</param>
    [Theory]
    [MemberData(nameof(IconPathData))]
    internal void Parse_アイコンのパスデータを解釈できる(string pathData)
    {
        using var path = SvgPathParser.Parse(pathData);

        Assert.True(path.PointCount > 0);
        Assert.True(CountFigures(path) > 0);
    }

    [Fact]
    internal void Parse_塗りつぶし規則は偶奇()
    {
        // アイコンの抜き（歯車のリング・中央の輪）は部分パスの重なり回数で表現されているため、
        // 偶奇規則で描画する必要があります。
        using var path = SvgPathParser.Parse("M0 0 L10 0 L10 10 Z");

        Assert.Equal(FillMode.Alternate, path.FillMode);
    }

    [Fact]
    internal void Parse_四角形は4点1図形になる()
    {
        using var path = SvgPathParser.Parse("M0 0 L10 0 L10 10 L0 10 Z");

        Assert.Equal(1, CountFigures(path));
        Assert.Equal(4, path.PointCount);
    }

    [Fact]
    internal void Parse_水平垂直コマンドを解釈する()
    {
        using var path = SvgPathParser.Parse("M1 2 H11 V22 Z");

        var points = path.PathPoints;
        Assert.Equal(1f, points[0].X);
        Assert.Equal(2f, points[0].Y);
        Assert.Equal(11f, points[1].X);
        Assert.Equal(2f, points[1].Y);
        Assert.Equal(11f, points[2].X);
        Assert.Equal(22f, points[2].Y);
    }

    [Fact]
    internal void Parse_相対コマンドを現在点からの差分として解釈する()
    {
        using var path = SvgPathParser.Parse("m10 10 l5 0 l0 5 z");

        var points = path.PathPoints;
        Assert.Equal(10f, points[0].X);
        Assert.Equal(10f, points[0].Y);
        Assert.Equal(15f, points[1].X);
        Assert.Equal(10f, points[1].Y);
        Assert.Equal(15f, points[2].X);
        Assert.Equal(15f, points[2].Y);
    }

    [Fact]
    internal void Parse_数値の区切りがない場合も解釈する()
    {
        // SVG は "M10 10L5 0" のようにコマンドと数値が密着していても有効です。
        using var path = SvgPathParser.Parse("M10 10L5 0");

        Assert.Equal(10f, path.PathPoints[0].X);
        Assert.Equal(5f, path.PathPoints[1].X);
    }

    [Fact]
    internal void Parse_負の数値と小数を解釈する()
    {
        using var path = SvgPathParser.Parse("M-1.5 -2.25L3.75 4");

        Assert.Equal(-1.5f, path.PathPoints[0].X);
        Assert.Equal(-2.25f, path.PathPoints[0].Y);
        Assert.Equal(3.75f, path.PathPoints[1].X);
        Assert.Equal(4f, path.PathPoints[1].Y);
    }

    [Fact]
    internal void Parse_Mの後の数値のみの並びは直線として扱う()
    {
        // SVG の仕様により、M の後に続く座標のみの指定は L（直線）と解釈します。
        using var path = SvgPathParser.Parse("M0 0 10 0 20 0");

        Assert.Equal(3, path.PointCount);
        Assert.Equal(20f, path.PathPoints[2].X);
    }

    [Fact]
    internal void Parse_三次ベジエは始点を含む4点になる()
    {
        using var path = SvgPathParser.Parse("M0 0 C1 1 2 2 3 3");

        var points = path.PathPoints;
        Assert.Equal(4, points.Length);
        Assert.Equal(0f, points[0].X);
        Assert.Equal(3f, points[3].X);
        Assert.Equal(3f, points[3].Y);
    }

    [Fact]
    internal void Parse_連続するSコマンドは制御点を反射する()
    {
        // S は直前の 3 次ベジエの 2 番目の制御点を始点に対して反転した点を第 1 制御点にします。
        using var path = SvgPathParser.Parse("M0 0 C0 10 10 10 10 0 S20 -10 20 0");

        var points = path.PathPoints;
        // 1 つ目のベジエ: 0..3 点、2 つ目のベジエ: 3..7 点（3 点目は共有）
        Assert.Equal(7, points.Length);
        // 反射後の制御点は (10, -10)
        Assert.Equal(10f, points[4].X);
        Assert.Equal(-10f, points[4].Y);
    }

    [Fact]
    internal void Parse_直前が三次ベジエでないSコマンドは制御点を始点と同一にする()
    {
        using var path = SvgPathParser.Parse("M5 5 S10 10 15 5");

        var points = path.PathPoints;
        Assert.Equal(5f, points[1].X);
        Assert.Equal(5f, points[1].Y);
        Assert.Equal(10f, points[2].X);
        Assert.Equal(10f, points[2].Y);
    }

    [Fact]
    internal void Parse_複数の図形をそれぞれ開始する()
    {
        using var path = SvgPathParser.Parse("M0 0 L10 0 L10 10 Z M20 0 L30 0 L30 10 Z");

        Assert.Equal(2, CountFigures(path));
    }

    [Fact]
    internal void Parse_円弧コマンドは未対応として通知する()
    {
        var exception = Assert.Throws<NotSupportedException>(() => SvgPathParser.Parse("M0 0 A5 5 0 0 1 10 10"));

        Assert.Contains("円弧", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    internal void Parse_不正なコマンドは形式エラーとして通知する()
    {
        Assert.Throws<FormatException>(() => SvgPathParser.Parse("X0 0"));
    }

    [Fact]
    internal void Parse_先頭がコマンドでない場合は形式エラーとして通知する()
    {
        Assert.Throws<FormatException>(() => SvgPathParser.Parse("0 0 L10 10"));
    }

    [Fact]
    internal void Parse_数値が不足している場合は形式エラーとして通知する()
    {
        Assert.Throws<FormatException>(() => SvgPathParser.Parse("M0 0 L10"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    internal void Parse_空の入力は引数例外として通知する(string? pathData)
    {
        // null の場合は ArgumentNullException（ArgumentException の派生）になるため、派生型も許容します。
        Assert.ThrowsAny<ArgumentException>(() => SvgPathParser.Parse(pathData!));
    }

    /// <summary>図形パスに含まれる部分図形の数を数えます。</summary>
    /// <param name="path">数える対象の図形パス。</param>
    /// <returns>部分図形の数。</returns>
    /// <remarks><see cref="GraphicsPath"/> 自体は図形数を公開しないため、イテレータの部分パス数を使用します。</remarks>
    private static int CountFigures(GraphicsPath path)
    {
        using var iterator = new GraphicsPathIterator(path);
        return iterator.SubpathCount;
    }
}
