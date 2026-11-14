using System;
using EsUtil.ZenHanConverter.App.Core.Helpers;
using Xunit;

namespace EsUtil.ZenHanConverter.Tests.App.Core.Helpers;

/// <summary><see cref="AppVersion"/> の仕様テスト（製品バージョンから EsUtil の版を読む）。</summary>
public sealed class AppVersionTests
{
    [Fact]
    internal void For_はUI名と版を改行で分ける()
    {
        // ナビゲーション ペインのフッターは 2 行（UI 名 / v 付きの版）で表示する
        Assert.Equal($"MewUI{Environment.NewLine}v{AppVersion.Current}", AppVersion.For("MewUI"));
        Assert.Equal($"v{AppVersion.Current}", AppVersion.For(""));
    }

    [Fact]
    internal void EsUtilVersion_は版だけを返しコミットを含めない()
    {
        // 製品バージョンは `0.5.0+esutil.0.6.5`（SourceLink が有効な場合は末尾へコミット SHA が付く）。
        // 版の桁だけを返す（`unknown` は製品バージョンに埋め込まれていない場合）
        Assert.False(string.Equals(AppVersion.Unknown, AppVersion.EsUtilVersion, StringComparison.Ordinal));
        Assert.Matches(@"^\d+\.\d+\.\d+", AppVersion.EsUtilVersion);
        Assert.DoesNotMatch(@"\.\d+\.\d+\.\d+\.[0-9a-f]{40}$", AppVersion.EsUtilVersion);
    }
}



