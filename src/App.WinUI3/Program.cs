using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using EsUtil.Platform.Core.Common;
using EsUtil.Ui.WinUI3.Interop.Bootstrapping;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinRT;

namespace EsUtil.ZenHanConverter.App.WinUI3;

/// <summary>アプリケーションのエントリ ポイントです。</summary>
/// <remarks>
/// Windows App Runtime の有無の確認と導入を <see cref="WindowsAppRuntimeBootstrap"/> に任せてから
/// WinUI を初期化します（XAML の自動生成 Main は <c>DISABLE_XAML_GENERATED_MAIN</c> で無効にしています）。<br/>
/// ランタイムが無い PC でも起動できるよう、WinUI の型へ触れる前（<see cref="Application.Start"/> の前）に
/// 用意します。<br/>
/// <b>診断</b>: 環境変数 <c>ZENHANCONVERTER_STARTUP_LOG</c> にファイルのパスを設定すると、起動と終了の
/// 通過点をそのファイルへ書き出します（CI の配布物検証が異常終了の原因を調べるために使う。 未設定なら何もしない）。
/// </remarks>
public static class Program
{
    /// <summary>起動トレースの出力先（環境変数 <c>ZENHANCONVERTER_STARTUP_LOG</c> の値）。</summary>
    private static readonly string? s_startupLog = Environment.GetEnvironmentVariable("ZENHANCONVERTER_STARTUP_LOG");

    /// <summary>アプリケーションを開始します。</summary>
    [STAThread]
    private static void Main()
    {
        Trace("Main: start");

        try
        {
            if (!WindowsAppRuntimeBootstrap.TryEnsureAvailable())
            {
                Trace("Main: runtime is not available");
                return;
            }

            Trace("Main: runtime is ready");

            ComWrappersSupport.InitializeComWrappers();
            Application.Start(p =>
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                _ = new App();
            });

            Trace("Main: message loop ended");
        }
        finally
        {
            // 終了時の解放に失敗してもプロセスを異常終了させない（Native AOT では未処理の例外が
            // そのまま異常終了になるため、ここで止める）。
            try
            {
                WindowsAppRuntimeBootstrap.Shutdown();
                Trace("Main: shutdown completed");
            }
            catch (Exception ex) when (BoundaryCatch.IsRecoverable(ex))
            {
                Trace($"Main: shutdown failed: {ex}");
                Debug.WriteLine($"[起動終了]: ランタイムの解放に失敗しました: {ex.Message}");
            }
        }
    }

    /// <summary>起動トレースをファイルへ書き出します（環境変数が未設定なら何もしません）。</summary>
    /// <param name="message">書き出す内容。</param>
    private static void Trace(string message)
    {
        if (string.IsNullOrEmpty(s_startupLog)) { return; }

        try
        {
            File.AppendAllText(s_startupLog, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"[起動トレース]: 書き込みに失敗しました: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"[起動トレース]: 書き込みが許可されていません: {ex.Message}");
        }
    }
}
