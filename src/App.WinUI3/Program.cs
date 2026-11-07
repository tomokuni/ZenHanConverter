using System;
using System.Threading;
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
/// 用意します。
/// </remarks>
public static class Program
{
    /// <summary>アプリケーションを開始します。</summary>
    [STAThread]
    private static void Main()
    {
        if (!WindowsAppRuntimeBootstrap.TryEnsureAvailable())
        {
            return;
        }

        try
        {
            ComWrappersSupport.InitializeComWrappers();
            Application.Start(p =>
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                _ = new App();
            });
        }
        finally
        {
            WindowsAppRuntimeBootstrap.Shutdown();
        }
    }
}
