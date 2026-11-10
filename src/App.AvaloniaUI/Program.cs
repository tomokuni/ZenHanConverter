using Avalonia;
using EsUtil.ZenHanConverter.App.Core.Logging;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace EsUtil.ZenHanConverter.App.AvaloniaUI;

/// <summary>アプリケーションのエントリポイントを提供します。</summary>
/// <remarks>
/// 提供機能: <br/>
/// - Avalonia アプリケーションの構成と起動<br/>
/// - 集約エラーハンドラー（バックグラウンドスレッド、非同期タスク）の購読<br/><br/>
/// 特徴: <br/>
/// - Avalonia の初期化前に Avalonia 依存の API を触らないようにするため、UI スレッドの例外は
///   <see cref="App"/> 側で購読します
/// </remarks>
public static class Program
{
    /// <summary>アプリケーションのエントリポイントです。</summary>
    /// <param name="args">起動引数。</param>
    [STAThread]
    public static void Main(string[] args)
    {
        // ログを最初に用意する（以降の失敗を残せる。 出力先とファイル名は .github/ログ規約.md）
        s_logger = AppLog
            .Initialize(typeof(Program).Assembly.GetName().Name ?? "ZenHanConverter.AvaloniaUI", "[App.AvaloniaUI]")
            .CreateLogger(nameof(Program));
        s_logger.LogAppStart(typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown");
        s_logger.LogStartupEnvironment(0);

        SubscribeExceptionHandlers();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

        s_logger.LogAppExit(0);
        AppLog.Shutdown();
    }

    /// <summary>Avalonia アプリケーションを構成します。ビジュアルデザイナからも参照されます。</summary>
    /// <returns>構成済みの <see cref="AppBuilder"/>。</returns>
    /// <remarks>開発者ツールは Debug 構成でのみ有効にします（Release へは含めません）。</remarks>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();

    /// <summary>集約エラーハンドラーを購読します。バックグラウンド/非同期タスクの未処理例外を補足します。</summary>
    private static void SubscribeExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                s_logger.LogUnhandledException(ex, "AppDomain.UnhandledException");
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            s_logger.LogUnhandledException(e.Exception, "TaskScheduler.UnobservedTaskException");
            e.SetObserved();
        };
    }

    /// <summary>ロガー（例外ハンドラーと起動・終了の記録に使う）。</summary>
    private static ILogger s_logger = null!;
}
