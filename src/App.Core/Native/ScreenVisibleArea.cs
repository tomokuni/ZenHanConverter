using EsUtil.Platform.Core.Coord;
using EsUtil.Platform.Core.Geometry;
using EsUtil.Platform.Core.Native.Win;
using System;

namespace EsUtil.ZenHanConverter.App.Core.Native;

/// <summary>仮想画面（全モニタの外接矩形）に基づくウィンドウ位置の補正を提供する静的クラス。</summary>
/// <remarks>
/// 提供機能: <br/>
/// - 仮想画面の外接矩形を DIP で取得<br/>
/// - 保存されたウィンドウ位置を表示領域内へ補正<br/>
/// - DIP と物理ピクセルの換算（丸め規則を一元管理）<br/><br/>
/// 特徴: <br/>
/// - Win32 API（<see cref="WinVirtualScreen"/>）と補正ロジック（<see cref="Helpers.WindowPlacement"/>）の組み合わせを
///   単一所有し、各 UI のウィンドウ復元処理から重複を排除する<br/>
/// - 補正ロジック自体は <see cref="Helpers.WindowPlacement"/> が純粋関数として所有し、本クラスは Win32 依存部分のみを担う<br/>
/// - DPI スケールの正規化は共通基盤の <see cref="DpiScaler.NormalizeScaling"/> が単一所有する<br/><br/>
/// 注意点: <br/>
/// - <see cref="WinVirtualScreen"/> は Windows 専用のため、本クラスも Windows でのみ動作します
/// </remarks>
public static class ScreenVisibleArea
{
    /// <summary>仮想画面の外接矩形を DIP（論理ピクセル）で取得します。</summary>
    /// <param name="scale">DIP から物理ピクセルへの換算に使用する DPI スケール。0 以下は 100% とみなします。</param>
    /// <returns>仮想画面の外接矩形（DIP、負座標を含みます）。</returns>
    public static EsRect GetVisibleArea(double scale)
    {
        var bounds = WinVirtualScreen.Bounds;
        var dpiScale = DpiScaler.NormalizeScaling(scale);

        return new EsRect(
            bounds.X / dpiScale,
            bounds.Y / dpiScale,
            bounds.Width / dpiScale,
            bounds.Height / dpiScale);
    }

    /// <summary>保存されたウィンドウ位置を表示領域内へ補正します。</summary>
    /// <param name="saved">保存されたウィンドウ左上の位置（画面座標・DIP）。</param>
    /// <param name="windowSize">ウィンドウのサイズ（DIP）。</param>
    /// <param name="scale">DIP から物理ピクセルへの換算に使用する DPI スケール。0 以下は 100% とみなします。</param>
    /// <returns>補正後のウィンドウ左上の位置（DIP）。</returns>
    public static EsPoint Clamp(EsPoint saved, EsSize windowSize, double scale)
        => Helpers.WindowPlacement.ClampToVisibleArea(saved, windowSize, GetVisibleArea(scale));

    /// <summary>DIP の値を物理ピクセルへ換算します。</summary>
    /// <param name="dip">DIP（論理ピクセル）の値。</param>
    /// <param name="scale">DIP から物理ピクセルへの換算に使用する DPI スケール。0 以下は 100% とみなします。</param>
    /// <returns>物理ピクセル値（四捨五入）。</returns>
    /// <remarks>丸め規則（四捨五入）を一元管理し、ウィンドウの位置とサイズで規則がずれないようにします。</remarks>
    public static int ToPhysical(double dip, double scale)
        => (int)Math.Round(dip * DpiScaler.NormalizeScaling(scale));

    /// <summary>物理ピクセルの値を DIP へ換算します。</summary>
    /// <param name="physical">物理ピクセルの値。</param>
    /// <param name="scale">DIP から物理ピクセルへの換算に使用する DPI スケール。0 以下は 100% とみなします。</param>
    /// <returns>DIP（論理ピクセル）の値。</returns>
    public static double ToDip(int physical, double scale) => physical / DpiScaler.NormalizeScaling(scale);
}
