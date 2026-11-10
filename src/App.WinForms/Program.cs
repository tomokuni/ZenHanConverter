using EsUtil.ZenHanConverter.App.Core.Logging;
using EsUtil.ZenHanConverter.App.WinForms.Services;
using EsUtil.ZenHanConverter.App.WinForms.Views;
using EsUtil.ZenHanConverter.App.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace EsUtil.ZenHanConverter.App.WinForms;

/// <summary>アプリケーションのエントリポイントを提供します。</summary>
/// <remarks>
/// 提供機能: <br/>
/// - WinForms アプリケーションの構成と起動<br/>
/// - DI コンテナ（Microsoft.Extensions.Hosting）の初期化とサービス登録<br/>
/// - 集約エラーハンドラー（UI スレッド、バックグラウンドスレッド、非同期タスク）<br/><br/>
/// 特徴: <br/>
/// - サービス登録は <see cref="DependencyInjectionExtensions"/> に分離（単一責任の原則）<br/>
/// - <see cref="ApplicationConfiguration.Initialize"/> が csproj の設定に従って
///   DPI 認識・ビジュアルスタイル・テキストレンダリング方式を初期化する
/// </remarks>
public static class Program
{
    /// <summary>DI コンテナのサービスプロバイダーを取得します。</summary>
    /// <value>ビルド済みの IServiceProvider インスタンス。</value>
    private static IServiceProvider s_services { get; set; } = null!;

    /// <summary>ロガー（例外ハンドラーと起動・終了の記録に使う）。</summary>
    private static ILogger s_logger = null!;

    /// <summary>アプリケーションのエントリポイントです。</summary>
    [STAThread]
    public static void Main()
    {
        // ログを最初に用意する（以降の失敗を残せる。 出力先とファイル名は .github/ログ規約.md）
        s_logger = AppLog
            .Initialize(typeof(Program).Assembly.GetName().Name ?? "ZenHanConverter.WinForms", "[App.WinForms]")
            .CreateLogger(nameof(Program));
        s_logger.LogAppStart(typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown");

        ApplicationConfiguration.Initialize();
        SubscribeExceptionHandlers();

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddZenHanConverterServices();
        var host = builder.Build();

        s_services = host.Services;
        // 旧名のフォルダー（%LOCALAPPDATA%\ClipboardZenHanConverter）から初回だけ移行する
        AppPaths.Migrate();
        GetService<AppSetting>().Initialize();
        GetService<ConvertConfig>().Initialize();

        var mainForm = GetService<MainForm>();
        // 倍率が確定してから環境を 1 行だけ記録する（.github/ログ規約.md）
        s_logger.LogStartupEnvironment(mainForm.DeviceDpi / 96.0);

        Application.Run(mainForm);
        s_logger.LogAppExit(0);
        AppLog.Shutdown();
    }

    /// <summary>DI コンテナから指定した型のサービスを取得します。</summary>
    /// <typeparam name="T">取得するサービスの型。</typeparam>
    /// <returns>登録されたサービスインスタンス。</returns>
    /// <exception cref="InvalidOperationException">指定された型が DI コンテナに登録されていない場合。</exception>
    private static T GetService<T>() where T : class
        => s_services.GetService(typeof(T)) as T
            ?? throw new InvalidOperationException($"{typeof(T)} is not registered in DI container.");

    /// <summary>集約エラーハンドラーを購読します。UI/バックグラウンド/非同期タスクの未処理例外を補足します。</summary>
    private static void SubscribeExceptionHandlers()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        Application.ThreadException += (_, e) => s_logger.LogUnhandledException(e.Exception, "Application.ThreadException");
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
}
