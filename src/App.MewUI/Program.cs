using Aprillz.MewUI;
using EsUtil.ZenHanConverter.App.Core.Logging;
using EsUtil.ZenHanConverter.App.MewUI.Services;
using EsUtil.ZenHanConverter.App.MewUI.Views;
using EsUtil.ZenHanConverter.App.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace EsUtil.ZenHanConverter.App.MewUI;

/// <summary>アプリケーションのエントリポイントを提供します。</summary>
/// <remarks>
/// 提供機能: <br/>
/// - DIコンテナ（Microsoft.Extensions.Hosting）の初期化とサービス登録<br/>
/// - 集約エラーハンドラー（UIスレッド、バックグラウンドスレッド、非同期タスク）<br/>
/// - 起動時のメインウィンドウ表示<br/><br/>
/// 特徴: <br/>
/// - サービス登録は DependencyInjectionExtensions に分離（単一責任の原則）<br/>
/// - MewUI の ApplicationBuilder によるウィンドウ構築
/// </remarks>
public static class Program
{
    /// <summary>アプリケーションのエントリポイントです。</summary>
    public static void Main()
    {
        // ログを最初に用意する（以降の失敗を残せる。 出力先とファイル名は .github/ログ規約.md）
        s_logger = AppLog
            .Initialize(typeof(Program).Assembly.GetName().Name ?? "ZenHanConverter.MewUI", "[App.MewUI]")
            .CreateLogger(nameof(Program));
        s_logger.LogAppStart(typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown");

        SubscribeExceptionHandlers();

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddZenHanConverterServices();
        var host = builder.Build();

        // 設定を読み込み、自動保存を開始
        // 旧名のフォルダー（%LOCALAPPDATA%\ClipboardZenHanConverter）から初回だけ移行する
        AppPaths.Migrate();
        host.Services.GetRequiredService<AppSetting>().Initialize();
        host.Services.GetRequiredService<ConvertConfig>().Initialize();

        // 環境を 1 行だけ記録する（.github/ログ規約.md）
        s_logger.LogStartupEnvironment(0);

        Application
            .Create()
            .UseWin32()
            .UseDirect2D()
            .BuildMainWindow(() => host.Services.GetRequiredService<MainWindow>())
            .Run();

        s_logger.LogAppExit(0);
        AppLog.Shutdown();
    }

    /// <summary>集約エラーハンドラーを購読します。UI/バックグラウンド/非同期タスクの未処理例外を補足します。</summary>
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
        Application.DispatcherUnhandledException += e =>
        {
            s_logger.LogUnhandledException(e.Exception, "DispatcherUnhandledException");
            e.Handled = true;
        };
    }

    /// <summary>ロガー（例外ハンドラーと起動・終了の記録に使う）。</summary>
    private static ILogger s_logger = null!;
}
