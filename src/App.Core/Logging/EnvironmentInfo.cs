using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using EsUtil.Platform.Core;
using EsUtil.Platform.Core.Geometry;
using EsUtil.Platform.Core.Native.Win;
using Microsoft.Win32;

namespace EsUtil.ZenHanConverter.App.Core.Logging;

/// <summary>実行環境（OS・表示経路・倍率・GPU）を診断用の値として取得します。</summary>
/// <remarks>
/// <b>なぜ必要か</b>: 描画の不具合は「アプリの値」ではなく環境（リモート セッション・GPU・倍率）で決まることがあります。<br/>
/// 環境を 1 行で残せば、次回からログだけで切り分けられます（規則の所有者は <c>.github/ログ規約.md</c>）。<br/>
/// <b>失敗を例外にしない</b>: 診断のための値であり、取得できなくても起動・描画を妨げません。
/// </remarks>
public static class EnvironmentInfo
{
    /// <summary>モニターの DPI の照会（コアが単一所有する実装を使います）。</summary>
    private static readonly WinMonitorDpi s_monitorDpi = new();

    /// <summary>表示アダプターが登録されるレジストリのクラス（<c>Display</c>）。</summary>
    private const string DisplayClassPath =
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    /// <summary>仮想スクリーン（全モニターを包含する矩形）を取得します。</summary>
    /// <returns>「<c>x,y 幅x高さ</c>」（画面物理ピクセル）。</returns>
    public static string DescribeVirtualScreen()
    {
        var bounds = WinVirtualScreen.Bounds;
        return string.Create(CultureInfo.InvariantCulture, $"{bounds.X},{bounds.Y} {bounds.Width}x{bounds.Height}");
    }

    /// <summary>仮想スクリーンの位置からモニターの表示倍率を解決します。</summary>
    /// <returns>重複を除いた倍率（例: <c>1.25x, 1x</c>）。 解決できない場合は <c>(不明)</c>。</returns>
    public static string DescribeMonitorScales()
    {
        var bounds = WinVirtualScreen.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return "(不明)";
        }

        EsPointInt[] points =
        [
            new(bounds.X + 1, bounds.Y + 1),
            new(bounds.X + 1, bounds.Y + bounds.Height - 2),
            new(bounds.X + bounds.Width - 2, bounds.Y + 1),
            new(bounds.X + bounds.Width - 2, bounds.Y + bounds.Height - 2),
            new(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2)),
        ];

        var scales = new SortedSet<double>();
        foreach (var point in points)
        {
            if (s_monitorDpi.GetEffectiveDpi(point) is { } dpi && dpi > 0)
            {
                scales.Add(dpi / WinMonitorDpi.StandardDpi);
            }
        }

        return scales.Count == 0
            ? "(不明)"
            : string.Join(", ", scales.Select(scale => scale.ToString("0.##", CultureInfo.InvariantCulture) + "x"));
    }

    /// <summary>表示アダプター（ドライバーの説明と VRAM）を取得します。</summary>
    /// <returns>「<c>説明 (VRAM MB) / 説明2 …</c>」。 取得できない場合は <c>(不明)</c> またはその旨。</returns>
    public static string DescribeAdapters()
    {
        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(DisplayClassPath);
            if (classKey is null)
            {
                return "(不明)";
            }

            var descriptions = new System.Collections.Generic.List<string>();
            foreach (var name in classKey.GetSubKeyNames())
            {
                if (name.Length != 4 || !int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                {
                    continue;
                }

                using var key = classKey.OpenSubKey(name);
                if (key?.GetValue("DriverDesc") is not string description || description.Length == 0)
                {
                    continue;
                }

                descriptions.Add(ReadVramMegabytes(key) is { } megabytes
                    ? string.Create(CultureInfo.InvariantCulture, $"{description} ({megabytes} MB)")
                    : description);
            }

            return descriptions.Count == 0 ? "(不明)" : string.Join(" / ", descriptions);
        }
        catch (Exception ex) when (EsErrors.IsRecoverable(ex))
        {
            // 診断用の値であり、読めなくても起動・描画を妨げない
            return "(取得できませんでした)";
        }
    }

    /// <summary>アダプターの VRAM をメガバイトで取得します。</summary>
    /// <param name="key">アダプターのレジストリ キー。</param>
    /// <returns>VRAM（MB）。 登録が無い・読めない場合は <see langword="null"/>。</returns>
    private static long? ReadVramMegabytes(RegistryKey key)
    {
        var bytes = key.GetValue(@"HardwareInformation.qwMemorySize") switch
        {
            long value => value,
            int value => value,
            byte[] { Length: >= 8 } array => BitConverter.ToInt64(array, 0),
            _ => 0L,
        };

        return bytes > 0 ? bytes / (1024 * 1024) : null;
    }

    /// <summary>プロセスのアーキテクチャを取得します。</summary>
    /// <returns>「<c>x64</c>」などの名前。</returns>
    /// <remarks>OS の版と併せて記録します（32bit で動いていないかを判別するため）。</remarks>
    public static string Architecture => RuntimeInformation.ProcessArchitecture.ToString();
}
