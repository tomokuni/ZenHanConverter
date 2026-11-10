using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EsUtil.Platform.Core.Common;
using EsUtil.ZenHanConverter.App.Core.Logging;
using Microsoft.Extensions.Logging;
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

        // 自己完結の Single 版では Windows App Runtime の展開先を自分で指す必要がある。
        // Windows App SDK の単一ファイル配布は `MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY` を
        // **プログラムの開始前**に設定することを要求する（設定しないと Dll の探索に失敗する）。
        // **ビルド時の定数で切り替える**: 実行時に「単一ファイルか」を判定すると `Assembly.Location` が
        // 空になることを使うしかなく、トリミング解析が IL3000 を報告する（抑制属性が必要になる）。
        // 自己完結かどうかはビルド構成で決まるため、定数のほうが確実で抑止も要らない。
        // フレームワーク依存の配置では同封物が無いため設定しない（設定すると探索先が変わる）。
#if ZENHANCONVERTER_WINDOWSAPPSDK_BUNDLED
        Environment.SetEnvironmentVariable(
            "MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", AppContext.BaseDirectory);
#endif

        // ログを最初に用意する（以降の失敗を残せるようにする。 出力先とファイル名は .github/ログ規約.md）
        var factory = AppLog.Initialize(AssemblyName, "[App.WinUI3]");
        var logger = factory.CreateLogger(nameof(Program));
        logger.LogAppStart(ReadVersion());
        RegisterExceptionLogging(logger);

        try
        {
            // **アプリのビルド版を要求する**: EsUtil は自分をビルドしたときの版を焼き込むため、指定しないと
            // アプリのマネージド アセンブリより古いネイティブ ランタイムで初期化が成功し得る（実測 2026-11-10）。
            var options = WindowsAppRuntimeBootstrapOptions.Default with
            {
                RequiredVersion = WindowsAppRuntimeRequirement.Version,
            };

            var required = WindowsAppRuntimeBootstrap.GetRequiredVersion(options).ToString(3);
            var before = DescribeInstalledVersions();

            if (!WindowsAppRuntimeBootstrap.IsRequiredVersionSpecified(options))
            {
                logger.LogWindowsAppRuntimeVersionNotSpecified(
                    WindowsAppRuntimeInventory.RequiredVersion.ToString(3));
            }

            if (!WindowsAppRuntimeBootstrap.TryEnsureAvailable(options))
            {
                logger.LogWindowsAppRuntimeUnavailable(required, before, DescribeInstalledVersions());
                Trace("Main: runtime is not available");
                return;
            }

            logger.LogWindowsAppRuntimeReady(required, before, DescribeInstalledVersions(),
                WindowsAppRuntimeBootstrap.IsInitialized);
            Trace("Main: runtime is ready");

            ComWrappersSupport.InitializeComWrappers();
            Application.Start(p =>
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                _ = new App();
            });

            Trace("Main: message loop ended");
            logger.LogAppExit(0);
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

            AppLog.Shutdown();
        }
    }

    /// <summary>この実行ファイルのアセンブリ名（ログ ファイル名に使います）。</summary>
    private static string AssemblyName => typeof(Program).Assembly.GetName().Name ?? "ZenHanConverter.WinUI3";

    /// <summary>未処理の例外をログへ記録するハンドラを登録します。</summary>
    /// <param name="logger">ロガー。</param>
    /// <remarks><c>AppDomain</c> と <c>TaskScheduler</c> の両方を記録します（どちらもプロセスを終了させ得るため）。</remarks>
    private static void RegisterExceptionLogging(ILogger logger)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            logger.LogUnhandledException(
                e.ExceptionObject as Exception ?? new InvalidOperationException("例外オブジェクトを取得できません"),
                "AppDomain.UnhandledException");
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            logger.LogUnhandledException(e.Exception, "TaskScheduler.UnobservedTaskException");
            e.SetObserved();
        };
    }

    /// <summary>実行ファイルの版を取得します。</summary>
    /// <returns>版の文字列。 取得できない場合は <c>unknown</c>。</returns>
    private static string ReadVersion()
        => typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";

    /// <summary>このビルドが要求する Windows App Runtime の版。</summary>
    /// <remarks>
    /// EsUtil は自分をビルドしたときの版を焼き込むため、そのまま任せるとアプリのマネージド アセンブリより
    /// 古いネイティブ ランタイムで初期化が成功します（実測 2026-11-10）。 アプリ側の版を明示して渡します。
    /// </remarks>
    private static class WindowsAppRuntimeRequirement
    {
        /// <summary>ビルドに使った <c>Microsoft.WindowsAppSDK</c> の版。</summary>
        /// <remarks><c>WindowsAppSdkIncludeVersionInfo</c> が生成する internal の定数から取ります。</remarks>
        public static Version Version { get; } = new(
            Microsoft.WindowsAppSDK.Runtime.Version.Major,
            Microsoft.WindowsAppSDK.Runtime.Version.Minor,
            Microsoft.WindowsAppSDK.Runtime.Version.Build,
            Microsoft.WindowsAppSDK.Runtime.Version.Revision);
    }

    /// <summary>インストール済みの Windows App Runtime の版を 1 行の文字列にします。</summary>
    /// <returns>新しい順の版。 1 件も無い場合は <c>(なし)</c>。</returns>
    private static string DescribeInstalledVersions()
    {
        var versions = WindowsAppRuntimeInventory.GetInstalledVersions();
        return versions.Count == 0
            ? "(なし)"
            : string.Join(", ", versions.Select(version => version.ToString(4)));
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
