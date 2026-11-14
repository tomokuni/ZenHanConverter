using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using EsUtil.ZenHanConverter.App.Core.Helpers;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace EsUtil.ZenHanConverter.App.Core.Logging;

/// <summary>アプリのログを組み立て、共通のログ メソッドを提供します。</summary>
/// <remarks>
/// <b>出力先</b>: <c>%LOCALAPPDATA%\ZenHanConverter\logs\&lt;アセンブリ名&gt;.&lt;yyyyMMdd&gt;.log</c>
/// （規則の所有者は <c>.github/ログ規約.md</c> §1）。 4 つの UI 版は設定を共有しますが、ログは
/// <b>実行ファイル（アセンブリ）ごとに分けます</b>（どの UI 版の記録かをファイル名で判別できるようにするため）。<br/>
/// <b>日付の位置</b>: Serilog は拡張子の直前に日付を挿入し、区切りを補いません。 そのためシンクへ渡すパスは
/// <b><c>&lt;アセンブリ名&gt;..log</c></b>（<c>.</c> が 2 つ）とします（実測 2026-11-09:
/// <c>ZenHanConverter.WinUI3.log</c> → <c>ZenHanConverter.WinUI320261109.log</c>）。<br/>
/// <b>レベルの表記</b>: <c>[TRC]</c> などの 3 文字（<see cref="LogLevelText"/> が単一所有します）。<br/>
/// <b>失敗を例外にしない</b>: ログを書けない環境（読み取り専用のフォルダー等）でも起動を妨げません。<br/>
/// <b>定義は <c>LoggerMessage</c> のソース生成を使います</b>（CA1848。 構造化ログの値もそのまま渡せます）。
/// </remarks>
public static partial class AppLog
{
    /// <summary>ログの出力先フォルダー（<c>%LOCALAPPDATA%\ZenHanConverter\logs</c>）。</summary>
    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Models.AppPaths.ProductName,
        "logs");

    /// <summary>ログの接頭辞（<see cref="Initialize"/> が設定します）。</summary>
    public static string Prefix { get; private set; } = "[App]";

    /// <summary>ログ ファイルのパス（<c>&lt;アセンブリ名&gt;..log</c>。 日付は Serilog が挿入します）。</summary>
    /// <param name="assemblyName">実行ファイルのアセンブリ名。</param>
    /// <returns>シンクへ渡すパス。</returns>
    /// <remarks>拡張子の直前に Serilog が日付を入れるため、<c>.</c> を 2 つ置きます（<c>..log</c>）。</remarks>
    public static string GetLogFilePath(string assemblyName)
        => Path.Combine(Folder, assemblyName + "..log");

    /// <summary>カテゴリ名を指定してロガーを作成します。</summary>
    /// <param name="categoryName">ロガー カテゴリ名（例: <c>HomeViewModel</c>）。</param>
    /// <returns>ロガー。 <see cref="Initialize"/> より前や、ログを書けない環境では何も出力しません。</returns>
    /// <remarks>
    /// <b>初期化前に呼んでも安全です</b>: 未初期化（およびテスト）では <c>NullLogger</c> を返し、
    /// 呼び出し側の分岐を増やしません。
    /// </remarks>
    public static ILogger CreateLogger(string categoryName)
        => s_factory?.CreateLogger(categoryName) ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    /// <summary>実行環境の値（OS と .NET）を組み立てます。</summary>
    /// <returns>OS の版とアーキテクチャ、実行している .NET の版。</returns>
    /// <remarks>各 UI 版が同じ書式でログへ出せるように、変換をここへ集めます。</remarks>
    public static (string OperatingSystem, string DotNet) DescribeProcess()
        => (string.Create(
                CultureInfo.InvariantCulture,
                $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"{RuntimeInformation.FrameworkDescription} ({Environment.Version})"));

    /// <summary>ログを初期化し、ロガー ファクトリを返します。</summary>
    /// <param name="assemblyName">実行ファイルのアセンブリ名（ログ ファイル名に使います）。</param>
    /// <param name="prefix">接頭辞（例: <c>[App.WinUI3]</c>）。</param>
    /// <returns>ロガー ファクトリ。</returns>
    /// <remarks>
    /// 1 プロセスで 1 回だけ呼びます。 2 回目以降は既に作ったロガーを返します（同じファイルへ二重に書かないため）。<br/>
    /// 出力テンプレートは <c>{Timestamp} [{LogLevelText}] [{Prefix}] {Message}</c> とし、例外は改行して続けます。
    /// </remarks>
    public static ILoggerFactory Initialize(string assemblyName, string prefix)
    {
        ArgumentException.ThrowIfNullOrEmpty(assemblyName);
        ArgumentException.ThrowIfNullOrEmpty(prefix);

        lock (s_gate)
        {
            if (s_factory is not null)
            {
                return s_factory;
            }

            Prefix = prefix;
            try
            {
                Directory.CreateDirectory(Folder);
                s_serilog = new LoggerConfiguration()
                    .MinimumLevel.Verbose()
                    .Enrich.With(new LogLevelTextEnricher())
                    .WriteTo.File(
                        GetLogFilePath(assemblyName),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 7,
                        shared: false,
                        formatProvider: CultureInfo.InvariantCulture,
                        outputTemplate:
                            "{Timestamp:HH:mm:ss.fff} [{LogLevelText}] {Message:lj}{NewLine}{Exception}")
                    .CreateLogger();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // ログを書けない環境でも起動を妨げない（Debug 出力だけになる）
                s_serilog = null;
            }

            s_factory = LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Trace);
                if (s_serilog is not null)
                {
                    builder.AddSerilog(s_serilog, dispose: false);
                }
            });
            return s_factory;
        }
    }

    /// <summary>ログを終了し、ファイルを閉じます。</summary>
    public static void Shutdown()
    {
        lock (s_gate)
        {
            s_factory?.Dispose();
            s_factory = null;
            s_serilog?.Dispose();
            s_serilog = null;
        }
    }

    /// <summary>実行環境を 1 行だけ記録します（値を自分で集めます）。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="windowScale">ウィンドウの表示倍率（不明なら 0）。</param>
    /// <param name="runtimeInstalled">インストール済みの Windows App Runtime の版（使わない UI は <c>(対象外)</c>）。</param>
    /// <param name="runtimeRequired">要求する Windows App Runtime の版（使わない UI は <c>(対象外)</c>）。</param>
    /// <param name="runtimeLoaded">実際に読み込んでいる Windows App Runtime の版（使わない UI は <c>(対象外)</c>）。</param>
    /// <remarks>
    /// 値を集める処理は <see cref="EnvironmentInfo"/> が単一所有します。 各 UI 版はこれを 1 回呼ぶだけで、
    /// 同じ書式の 1 行が残ります（取得できない値は <c>(不明)</c> になります）。
    /// </remarks>
    public static void LogStartupEnvironment(
        this ILogger logger, double windowScale,
        string runtimeInstalled = "(対象外)", string runtimeRequired = "(対象外)", string runtimeLoaded = "(対象外)")
    {
        var (operatingSystem, dotNet) = DescribeProcess();
        logger.LogEnvironment(
            operatingSystem,
            dotNet,
            runtimeInstalled,
            runtimeRequired,
            runtimeLoaded,
            EnvironmentInfo.DescribeVirtualScreen(),
            windowScale,
            EnvironmentInfo.DescribeMonitorScales(),
            EnvironmentInfo.DescribeAdapters());
    }

    /// <summary>アプリの起動を記録します。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="version">アプリの版。</param>
    /// <remarks>LogLevel: Information / 出力書式: <c>アプリケーション起動 (version={Version})</c></remarks>
    public static void LogAppStart(this ILogger logger, string version) => logger.LogAppStartCore(Prefix, version, AppVersion.EsUtilVersion);

    /// <summary>アプリの終了を記録します。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="exitCode">終了コード。</param>
    /// <remarks>LogLevel: Information / 出力書式: <c>アプリケーション終了 (exitCode={ExitCode})</c></remarks>
    public static void LogAppExit(this ILogger logger, int exitCode) => logger.LogAppExitCore(Prefix, exitCode);

    /// <summary>クリップボードの変更を検出したことを記録します。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="length">取得したテキストの文字数。</param>
    /// <remarks>
    /// LogLevel: Debug / 出力書式: <c>クリップボードの変更を検出: 文字数={Length}</c><br/>
    /// <b>内容は記録しません</b>（利用者のコピーした文字列には機密が含まれ得るため。 文字数だけで切り分けられます）。
    /// </remarks>
    public static void LogClipboardChanged(this ILogger logger, int length)
        => logger.LogClipboardChangedCore(Prefix, length);

    /// <summary>クリップボードの変換結果を記録します。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="beforeLength">変換前の文字数。</param>
    /// <param name="afterLength">変換後の文字数。</param>
    /// <remarks>
    /// LogLevel: Debug / 出力書式: <c>クリップボードを変換しました: 文字数 {BeforeLength} → {AfterLength}</c><br/>
    /// <b>内容は記録しません</b>（利用者のコピーした文字列には機密が含まれ得るため）。
    /// </remarks>
    public static void LogClipboardConverted(this ILogger logger, int beforeLength, int afterLength)
        => logger.LogClipboardConvertedCore(Prefix, beforeLength, afterLength);

    /// <summary>クリップボード変換を中止した理由を記録します。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="reason">中止した理由。</param>
    /// <remarks>LogLevel: Debug / 出力書式: <c>クリップボード変換を中止: {Reason}</c></remarks>
    public static void LogClipboardConversionSkipped(this ILogger logger, string reason)
        => logger.LogClipboardConversionSkippedCore(Prefix, reason);

    /// <summary>クリップボードへアクセスできなかったことを記録します。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="ex">発生した例外。</param>
    /// <param name="operation">失敗した操作（読み取り / 書き戻し）。</param>
    /// <remarks>
    /// LogLevel: Warning / 出力書式: <c>クリップボードへアクセスできませんでした: {Operation}</c><br/>
    /// バックグラウンド時のアクセス拒否は想定内ですが、<b>握りつぶさず記録します</b>
    /// （「変換されない」報告をログから追えるようにするため。 [`.github/例外処理規約.md`](../../../.github/例外処理規約.md) §4）。
    /// </remarks>
    public static void LogClipboardAccessFailed(this ILogger logger, Exception ex, string operation)
        => logger.LogClipboardAccessFailedCore(Prefix, ex, operation);

    /// <summary>未処理の例外を記録します。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="ex">例外。</param>
    /// <param name="source">発生源（<c>AppDomain.UnhandledException</c> 等）。</param>
    /// <remarks>LogLevel: Critical / 出力書式: <c>未処理の例外: {Source}</c></remarks>
    public static void LogUnhandledException(this ILogger logger, Exception ex, string source)
        => logger.LogUnhandledExceptionCore(Prefix, ex, source);

    /// <summary>Windows App Runtime を用意できたことを記録します。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="required">要求した版。</param>
    /// <param name="before">用意する前にインストールされていた版。</param>
    /// <param name="after">用意した後にインストールされていた版。</param>
    /// <param name="initialized">Bootstrapper で初期化したか。</param>
    /// <remarks>
    /// LogLevel: Information <br/>
    /// 出力書式: <c>Windows App Runtime を用意しました (required={Required}, before={Before}, after={After}, initialized={Initialized})</c><br/>
    /// <b>成否と版を必ず残します</b>: 「入っていたのか」「今回入れたのか」が後から分からないと、環境の切り分けができません。
    /// </remarks>
    public static void LogWindowsAppRuntimeReady(
        this ILogger logger, string required, string before, string after, bool initialized)
        => logger.LogWindowsAppRuntimeReadyCore(Prefix, required, before, after, initialized);

    /// <summary>Windows App Runtime を用意できなかったことを記録します。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="required">要求した版。</param>
    /// <param name="before">用意する前にインストールされていた版。</param>
    /// <param name="after">用意した後にインストールされていた版。</param>
    /// <remarks>
    /// LogLevel: Warning <br/>
    /// 出力書式: <c>Windows App Runtime を用意できませんでした (required={Required}, before={Before}, after={After})</c>
    /// </remarks>
    public static void LogWindowsAppRuntimeUnavailable(
        this ILogger logger, string required, string before, string after)
        => logger.LogWindowsAppRuntimeUnavailableCore(Prefix, required, before, after);

    /// <summary>実行環境を記録します。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="operatingSystem">OS の版とアーキテクチャ。</param>
    /// <param name="dotNet">実行している .NET の版。</param>
    /// <param name="runtimeInstalled">インストール済みの Windows App Runtime の版。</param>
    /// <param name="runtimeRequired">要求する Windows App Runtime の版。</param>
    /// <param name="runtimeLoaded">実際に読み込んでいる Windows App Runtime の版。</param>
    /// <param name="virtualScreen">仮想スクリーンの矩形。</param>
    /// <param name="rasterizationScale">ウィンドウの表示倍率。</param>
    /// <param name="monitorScales">モニターの倍率。</param>
    /// <param name="adapters">表示アダプター。</param>
    /// <remarks>
    /// LogLevel: Information <br/>
    /// 出力書式: <c>環境情報: OS={OperatingSystem} / .NET={DotNet} / Windows App Runtime={RuntimeInstalled} (required={RuntimeRequired}, loaded={RuntimeLoaded}) / 仮想スクリーン={VirtualScreen} / ウィンドウ倍率={RasterizationScale} / モニター倍率={MonitorScales} / アダプター={Adapters}</c><br/>
    /// 描画の不具合はアプリの値ではなく環境（OS・ランタイムの版・GPU・倍率）で決まることがあります。
    /// 起動ごとに 1 回だけ出します（値は起動中に変わりません）。
    /// </remarks>
    public static void LogEnvironment(
        this ILogger logger, string operatingSystem, string dotNet,
        string runtimeInstalled, string runtimeRequired, string runtimeLoaded,
        string virtualScreen, double rasterizationScale, string monitorScales, string adapters)
        => logger.LogEnvironmentCore(
            Prefix, operatingSystem, dotNet, runtimeInstalled, runtimeRequired, runtimeLoaded,
            virtualScreen, rasterizationScale, monitorScales, adapters);

    /// <summary>要求する Windows App Runtime の版が指定されていないことを記録します。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="libraryVersion">EsUtil のビルド版（フォールバック先）。</param>
    /// <remarks>
    /// LogLevel: Warning / 出力書式: <c>要求する Windows App Runtime の版が指定されていません (library={LibraryVersion}) — アプリは自分のビルド版を指定してください</c><br/>
    /// 未指定は EsUtil のビルド版へフォールバックするため、アプリが EsUtil より新しい
    /// <c>Microsoft.WindowsAppSDK</c> でビルドされていると、アプリのマネージド アセンブリより古い
    /// ネイティブ ランタイムで初期化が成功し得ます（実測 2026-11-10）。
    /// </remarks>
    public static void LogWindowsAppRuntimeVersionNotSpecified(this ILogger logger, string libraryVersion)
        => logger.LogWindowsAppRuntimeVersionNotSpecifiedCore(Prefix, libraryVersion);

    /// <summary>要求する Windows App Runtime の版が指定されていないことを記録します。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="prefix">接頭辞。</param>
    /// <param name="libraryVersion">EsUtil のビルド版。</param>
    /// <remarks>
    /// LogLevel: Warning <br/>
    /// 出力書式: <c>{Prefix} 要求する Windows App Runtime の版が指定されていません (library={LibraryVersion}) — アプリは自分のビルド版を指定してください</c>
    /// </remarks>
    [LoggerMessage(LogLevel.Warning, "{Prefix} 要求する Windows App Runtime の版が指定されていません (library={LibraryVersion}) — アプリは自分のビルド版を指定してください")]
    private static partial void LogWindowsAppRuntimeVersionNotSpecifiedCore(
        this ILogger logger, string prefix, string libraryVersion);

    /// <summary>起動を記録します。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="prefix">接頭辞。</param>
    /// <param name="version">アプリの版。</param>
    /// <param name="esUtilVersion">使っている EsUtil の版。</param>
    /// <remarks>
    /// LogLevel: Information <br/>
    /// 出力書式: <c>{Prefix} アプリケーション起動 (version={Version}, esutil={EsUtilVersion})</c><br/>
    /// 使っている EsUtil の版も残します（パッケージ参照のため、ログだけでは版が分かりません）。
    /// </remarks>
    [LoggerMessage(LogLevel.Information, "{Prefix} アプリケーション起動 (version={Version}, esutil={EsUtilVersion})")]
    private static partial void LogAppStartCore(this ILogger logger, string prefix, string version, string esUtilVersion);

    /// <summary>終了を記録します。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="prefix">接頭辞。</param>
    /// <param name="exitCode">終了コード。</param>
    /// <remarks>
    /// LogLevel: Information <br/>
    /// 出力書式: <c>{Prefix} アプリケーション終了 (exitCode={ExitCode})</c>
    /// </remarks>
    [LoggerMessage(LogLevel.Information, "{Prefix} アプリケーション終了 (exitCode={ExitCode})")]
    private static partial void LogAppExitCore(this ILogger logger, string prefix, int exitCode);

    /// <summary>クリップボードの変更を検出したことを記録します。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="prefix">接頭辞。</param>
    /// <param name="length">取得したテキストの文字数。</param>
    /// <remarks>
    /// LogLevel: Debug <br/>
    /// 出力書式: <c>{Prefix} クリップボードの変更を検出: 文字数={Length}</c>
    /// </remarks>
    [LoggerMessage(LogLevel.Debug, "{Prefix} クリップボードの変更を検出: 文字数={Length}")]
    private static partial void LogClipboardChangedCore(this ILogger logger, string prefix, int length);

    /// <summary>クリップボードの変換結果を記録します。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="prefix">接頭辞。</param>
    /// <param name="beforeLength">変換前の文字数。</param>
    /// <param name="afterLength">変換後の文字数。</param>
    /// <remarks>
    /// LogLevel: Debug <br/>
    /// 出力書式: <c>{Prefix} クリップボードを変換しました: 文字数 {BeforeLength} → {AfterLength}</c>
    /// </remarks>
    [LoggerMessage(LogLevel.Debug, "{Prefix} クリップボードを変換しました: 文字数 {BeforeLength} → {AfterLength}")]
    private static partial void LogClipboardConvertedCore(this ILogger logger, string prefix, int beforeLength, int afterLength);

    /// <summary>クリップボード変換を中止した理由を記録します。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="prefix">接頭辞。</param>
    /// <param name="reason">中止した理由。</param>
    /// <remarks>
    /// LogLevel: Debug <br/>
    /// 出力書式: <c>{Prefix} クリップボード変換を中止: {Reason}</c>
    /// </remarks>
    [LoggerMessage(LogLevel.Debug, "{Prefix} クリップボード変換を中止: {Reason}")]
    private static partial void LogClipboardConversionSkippedCore(this ILogger logger, string prefix, string reason);

    /// <summary>クリップボードへアクセスできなかったことを記録します。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="prefix">接頭辞。</param>
    /// <param name="ex">発生した例外。</param>
    /// <param name="operation">失敗した操作。</param>
    /// <remarks>
    /// LogLevel: Warning <br/>
    /// 出力書式: <c>{Prefix} クリップボードへアクセスできませんでした: {Operation}</c>（例外はスタックトレース込み）
    /// </remarks>
    [LoggerMessage(LogLevel.Warning, "{Prefix} クリップボードへアクセスできませんでした: {Operation}")]
    private static partial void LogClipboardAccessFailedCore(this ILogger logger, string prefix, Exception ex, string operation);

    /// <summary>未処理の例外を記録します。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="prefix">接頭辞。</param>
    /// <param name="ex">例外。</param>
    /// <param name="source">発生源。</param>
    /// <remarks>
    /// LogLevel: Critical <br/>
    /// 出力書式: <c>{Prefix} 未処理の例外: {Source}</c>
    /// </remarks>
    [LoggerMessage(LogLevel.Critical, "{Prefix} 未処理の例外: {Source}")]
    private static partial void LogUnhandledExceptionCore(
        this ILogger logger, string prefix, Exception ex, string source);

    /// <summary>ランタイムを用意できたことを記録します。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="prefix">接頭辞。</param>
    /// <param name="required">要求した版。</param>
    /// <param name="before">用意する前の版。</param>
    /// <param name="after">用意した後の版。</param>
    /// <param name="initialized">初期化したか。</param>
    /// <remarks>
    /// LogLevel: Information <br/>
    /// 出力書式: <c>{Prefix} Windows App Runtime を用意しました (required={Required}, before={Before}, after={After}, initialized={Initialized})</c>
    /// </remarks>
    [LoggerMessage(LogLevel.Information, "{Prefix} Windows App Runtime を用意しました (required={Required}, before={Before}, after={After}, initialized={Initialized})")]
    private static partial void LogWindowsAppRuntimeReadyCore(
        this ILogger logger, string prefix, string required, string before, string after, bool initialized);

    /// <summary>ランタイムを用意できなかったことを記録します。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="prefix">接頭辞。</param>
    /// <param name="required">要求した版。</param>
    /// <param name="before">用意する前の版。</param>
    /// <param name="after">用意した後の版。</param>
    /// <remarks>
    /// LogLevel: Warning <br/>
    /// 出力書式: <c>{Prefix} Windows App Runtime を用意できませんでした (required={Required}, before={Before}, after={After})</c>
    /// </remarks>
    [LoggerMessage(LogLevel.Warning, "{Prefix} Windows App Runtime を用意できませんでした (required={Required}, before={Before}, after={After})")]
    private static partial void LogWindowsAppRuntimeUnavailableCore(
        this ILogger logger, string prefix, string required, string before, string after);

    /// <summary>実行環境を記録します。</summary>
    /// <param name="logger">ロガー。</param>
    /// <param name="prefix">接頭辞。</param>
    /// <param name="operatingSystem">OS の版。</param>
    /// <param name="dotNet">.NET の版。</param>
    /// <param name="runtimeInstalled">インストール済みのランタイムの版。</param>
    /// <param name="runtimeRequired">要求するランタイムの版。</param>
    /// <param name="runtimeLoaded">読み込んでいるランタイムの版。</param>
    /// <param name="virtualScreen">仮想スクリーン。</param>
    /// <param name="rasterizationScale">ウィンドウ倍率。</param>
    /// <param name="monitorScales">モニター倍率。</param>
    /// <param name="adapters">表示アダプター。</param>
    /// <remarks>
    /// LogLevel: Information <br/>
    /// 出力書式: <c>{Prefix} 環境情報: OS={OperatingSystem} / .NET={DotNet} / Windows App Runtime={RuntimeInstalled} (required={RuntimeRequired}, loaded={RuntimeLoaded}) / 仮想スクリーン={VirtualScreen} / ウィンドウ倍率={RasterizationScale} / モニター倍率={MonitorScales} / アダプター={Adapters}</c>
    /// </remarks>
    [LoggerMessage(LogLevel.Information, "{Prefix} 環境情報: OS={OperatingSystem} / .NET={DotNet} / Windows App Runtime={RuntimeInstalled} (required={RuntimeRequired}, loaded={RuntimeLoaded}) / 仮想スクリーン={VirtualScreen} / ウィンドウ倍率={RasterizationScale} / モニター倍率={MonitorScales} / アダプター={Adapters}")]
    private static partial void LogEnvironmentCore(
        this ILogger logger, string prefix,
        string operatingSystem, string dotNet, string runtimeInstalled, string runtimeRequired,
        string runtimeLoaded, string virtualScreen, double rasterizationScale,
        string monitorScales, string adapters);

    /// <summary>排他のためのゲート（初期化と終了を 1 度だけにします）。</summary>
    private static readonly Lock s_gate = new();

    /// <summary>Serilog のロガー（ファイルへの書き込み先）。</summary>
    private static Logger? s_serilog;

    /// <summary>Microsoft.Extensions.Logging のファクトリ。</summary>
    private static ILoggerFactory? s_factory;

    /// <summary>ログ イベントの <c>LogLevelText</c> プロパティを 3 文字の表記で埋めるエンリッチャー。</summary>
    /// <remarks>出力テンプレートの <c>{LogLevelText}</c> がこの値を使います（<see cref="LogLevelText"/> が規則を所有します）。</remarks>
    private sealed class LogLevelTextEnricher : ILogEventEnricher
    {
        /// <summary>ログ イベントへ <c>LogLevelText</c> を追加します。</summary>
        /// <param name="logEvent">対象のイベント。</param>
        /// <param name="propertyFactory">プロパティの生成元。</param>
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            ArgumentNullException.ThrowIfNull(logEvent);
            ArgumentNullException.ThrowIfNull(propertyFactory);
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(
                LogLevelText.PropertyName, LogLevelText.Abbreviate(logEvent.Level.ToString())));
        }
    }
}
