using EsUtil.ZenHanConverter.App.Core.Enums;
using EsUtil.ZenHanConverter.App.Core.Models;
using EsUtil.ZenHanConverter.App.Core.Presentation.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace EsUtil.ZenHanConverter.Tests.App.Core.Presentation.ViewModels;

/// <summary><see cref="ZenHanConvertItem"/> の設定との連動と表示状態を検証します。</summary>
public sealed class ZenHanConvertItemTests : IDisposable
{
    /// <summary>検証に使用する変換設定。</summary>
    private readonly ConvertConfig _config = TestHelper.CreateDefaultConfig();

    /// <summary>検証対象の変換項目。</summary>
    private readonly ZenHanConvertItem _item;

    /// <summary>テストインスタンスを初期化します。</summary>
    public ZenHanConvertItemTests()
    {
        _item = new ZenHanConvertItem(_config, SegmentDefinitions.NumberDefs[0]);
    }

    /// <summary>テスト項目を破棄します。</summary>
    public void Dispose()
    {
        _item.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    internal void Disposeは複数回呼び出しても例外を送出しない()
    {
        _item.Dispose();
        _item.Dispose();
    }

    [Fact]
    internal void Dispose後はConfig変更の通知を受け取らない()
    {
        _item.Dispose();

        var notified = false;
        _item.PropertyChanged += (_, _) => notified = true;
        _config.ConvertModeNumber = ZenHanMode.ToZen;

        Assert.False(notified);
    }

    [Fact]
    internal void Config変更でSelectedLabelの変更通知が発行される()
    {
        var changed = new List<string?>();
        _item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _config.ConvertModeNumber = ZenHanMode.ToZen;

        Assert.Contains(nameof(ZenHanConvertItem.SelectedLabel), changed, StringComparer.Ordinal);
    }

    [Fact]
    internal void Labelは定義のラベルを返す()
    {
        Assert.Equal(SegmentDefinitions.NumberDefs[0].Label, _item.Label);
    }

    [Fact]
    internal void Segments未指定の定義では既定の3つの選択肢を持つ()
    {
        Assert.Equal(3, _item.Options.Count);
        Assert.Equal("なし", _item.Options[0].Content);
        Assert.Equal("半角", _item.Options[1].Content);
        Assert.Equal("全角", _item.Options[2].Content);
    }

    [Fact]
    internal void Optionsは定義のセグメントと同じ表示テキストを順序を保って持つ()
    {
        var expected = SegmentDefinitions.NumberDefs[0].Segments is { Length: > 0 } segments
            ? segments.Select(s => s.Content)
            : ["なし", "半角", "全角"];

        Assert.Equal(expected, _item.Options.Select(o => o.Content), StringComparer.Ordinal);
    }

    [Fact]
    internal void OptionsのGroupNameは同一項目内で同じ()
    {
        Assert.All(_item.Options, o => Assert.Equal(_item.Options[0].GroupName, o.GroupName));
    }

    [Fact]
    internal void OptionsのGroupNameは項目間で異なる()
    {
        using var other = new ZenHanConvertItem(_config, SegmentDefinitions.AlphabetDefs[0]);

        Assert.NotEqual(_item.Options[0].GroupName, other.Options[0].GroupName, StringComparer.Ordinal);
    }

    [Fact]
    internal void IsEnabledはForceEnableState未指定では有効()
    {
        Assert.True(_item.IsEnabled);
        Assert.Null(_item.ForceEnableState);
    }

    [Fact]
    internal void IsEnabledはForceEnableStateに従う()
    {
        var def = new SegmentDefine("テスト", ConvertConfig.Mode.Number, ForceEnableState: false);
        using var item = new ZenHanConvertItem(_config, def);

        Assert.False(item.IsEnabled);
    }

    [Fact]
    internal void SelectedLabelは現在の設定値に対応するラベルを返す()
    {
        _config.ConvertModeNumber = ZenHanMode.ToZen;

        Assert.Equal("全角", _item.SelectedLabel);
    }

    [Fact]
    internal void SelectedLabelの設定は設定へ反映される()
    {
        _item.SelectedLabel = "全角";

        Assert.Equal(ZenHanMode.ToZen, _config.ConvertModeNumber);
    }

    [Fact]
    internal void Optionsの選択状態は現在の設定値を反映する()
    {
        _config.ConvertModeNumber = ZenHanMode.ToHan;

        Assert.False(_item.Options[0].IsSelected);
        Assert.True(_item.Options[1].IsSelected);
        Assert.False(_item.Options[2].IsSelected);
    }

    [Fact]
    internal void Optionsの選択変更は設定へ反映される()
    {
        // XAML では RadioButton の IsChecked が IsSelected へ双方向バインドされる
        _item.Options[2].IsSelected = true;

        Assert.Equal(ZenHanMode.ToZen, _config.ConvertModeNumber);
    }

    [Fact]
    internal void SelectedOptionは現在の設定値に対応する選択肢を返す()
    {
        _config.ConvertModeNumber = ZenHanMode.ToHan;

        Assert.Same(_item.Options[1], _item.SelectedOption);
    }

    [Fact]
    internal void SelectedOptionの設定は設定へ反映される()
    {
        // XAML ではセグメント コントロールの SelectedItem が SelectedOption へ双方向バインドされる
        _item.SelectedOption = _item.Options[2];

        Assert.Equal(ZenHanMode.ToZen, _config.ConvertModeNumber);
        Assert.Equal("全角", _item.SelectedLabel);
    }

    [Fact]
    internal void SelectedOptionへnullを設定しても設定は変わらない()
    {
        var before = _config.ConvertModeNumber;

        _item.SelectedOption = null;

        Assert.Equal(before, _config.ConvertModeNumber);
    }

    [Fact]
    internal void 外部から設定が変わるとSelectedOptionが追従する()
    {
        _config.ConvertModeNumber = ZenHanMode.ToZen;

        Assert.Same(_item.Options[2], _item.SelectedOption);
    }

    [Fact]
    internal void Config変更でSelectedOptionの変更通知が発行される()
    {
        var changed = new List<string?>();
        _item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _config.ConvertModeNumber = ZenHanMode.ToZen;

        Assert.Contains(nameof(ZenHanConvertItem.SelectedOption), changed, StringComparer.Ordinal);
    }

    [Fact]
    internal void 外部から設定が変わるとOptionsの選択状態が追従する()
    {
        _config.ConvertModeNumber = ZenHanMode.ToZen;

        Assert.False(_item.Options[0].IsSelected);
        Assert.False(_item.Options[1].IsSelected);
        Assert.True(_item.Options[2].IsSelected);
    }

    [Fact]
    internal void 無効な選択肢が選択されても設定は変わらない()
    {
        var def = new SegmentDefine("テスト", ConvertConfig.Mode.Number, ForceEnableState: false);
        using var item = new ZenHanConvertItem(_config, def);
        var disabled = item.Options.FirstOrDefault(o => !o.IsEnabled);
        if (disabled is null) return;

        var before = _config.ConvertModeNumber;
        disabled.IsSelected = true;

        Assert.Equal(before, _config.ConvertModeNumber);
    }
}
