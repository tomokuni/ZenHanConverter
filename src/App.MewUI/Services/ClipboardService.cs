using EsUtil.ZenHanConverter.App.Core.Interfaces;
using EsUtil.ZenHanConverter.App.Core.Logic;
using EsUtil.Platform.Core.Native.Win;
using Aprillz.MewUI;
using System;
using System.Threading.Tasks;

namespace EsUtil.ZenHanConverter.App.MewUI.Services;

/// <summary>システムクリップボードの読み書きと内容変更監視を提供します。</summary>
/// <remarks>
/// 提供機能: <br/>
/// - クリップボードのテキスト取得・設定<br/>
/// - クリップボードの内容変更監視（Win32 の <c>GetClipboardSequenceNumber</c> ポーリング）<br/>
/// - 書き戻し後のフラッシュ（アプリ終了後も内容を保持）<br/><br/>
/// 特徴: <br/>
/// - IClipboardService インターフェース実装（DIP）<br/>
/// - NativeAOT 対応（EsUtil.Platform.Core の WinClipboard へ委譲）<br/>
/// - 変更検出は Core の ClipboardChangeDetector へ委譲<br/>
/// - DispatcherTimer によるポーリングでクリップボード変更を検出<br/>
/// - アクセス拒否時などは握り潰してアプリ動作を継続<br/><br/>
/// 注意点: <br/>
/// - ポーリングは DispatcherTimer で行い、ContentChanged を UI スレッドで発行します。<br/>
///   購読側（HomeViewModel）がバインド済みのプロパティを更新するため、UI スレッドでの発行が必要です</remarks>
public sealed class ClipboardService : IClipboardService
{
    /// <summary>ポーリング間隔（ミリ秒）。Core の変更検出器が推奨値を単一所有します。</summary>
    private const int PollIntervalMs = ClipboardChangeDetector.RecommendedPollIntervalMs;

    /// <summary>クリップボード内容変更イベント。</summary>
    public event EventHandler<EventArgs>? ContentChanged;

    /// <summary>クリップボードの読み書き（EsUtil.Platform.Core の実装。 状態を持たないため 1 つを使い回す）。</summary>

    private readonly WinClipboard _clipboard = new();


    /// <summary>クリップボード内容の変更検出器。</summary>
    private readonly ClipboardChangeDetector _changeDetector;

    /// <summary>クリップボード変更監視のタイマー。</summary>
    private readonly DispatcherTimer _timer = new()
    {
        Interval = TimeSpan.FromMilliseconds(PollIntervalMs),
    };

    /// <summary>Dispose 済みのフラグ。</summary>
    private bool _disposed;

    /// <summary>ClipboardService の新しいインスタンスを初期化し、クリップボード監視を開始します。</summary>
    public ClipboardService()
    {
        _changeDetector = new(SafeGetSequence);
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    /// <summary>クリップボード変更を検出した場合に ContentChanged イベントを発行します。</summary>
    private void OnTimerTick()
    {
        if (_changeDetector.HasChanged())
            ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>クリップボードシーケンス番号を取得します。</summary>
    /// <remarks>取得は例外を投げません（失敗時は 0 を返す）。</remarks>
    private uint SafeGetSequence() => _clipboard.SequenceNumber;

    /// <summary>テスト用に ContentChanged イベントを発行します。</summary>
    public void RaiseContentChanged() => ContentChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>クリップボードからテキストを非同期に取得します。</summary>
    /// <returns>取得したテキスト。取得できない場合は null。</returns>
    public Task<string?> GetTextAsync()
        => Task.FromResult(_clipboard.TryReadText(out var text) ? text : null);

    /// <summary>指定されたテキストをクリップボードに設定します。</summary>
    /// <param name="text">設定するテキスト。</param>
    /// <remarks>バックグラウンド時などクリップボードへ書き込めない場合は何もしません（想定内の通常動作）。</remarks>
    public void SetText(string text) => _ = _clipboard.TryWriteText(text);

    /// <summary>クリップボードの内容をフラッシュ（永続化）します。</summary>
    public void Flush()
    {
        // CF_UNICODETEXT は SetClipboardData 時点で永続化されるため、明示的なフラッシュ操作は不要。
    }

    /// <summary>リソースを解放します。タイマーを停止し、イベント購読を解除します。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _timer.Tick -= OnTimerTick;
        _timer.Stop();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
