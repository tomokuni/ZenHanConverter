using EsUtil.ZenHanConverter.App.Core.Interfaces;
using EsUtil.ZenHanConverter.App.Core.Logging;
using EsUtil.ZenHanConverter.App.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using EsUtil.ZenHanConverter.App.Core.Presentation.ViewModels;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace EsUtil.ZenHanConverter.App.MewUI.ViewModels;

/// <summary>ホーム画面のデータを管理し、クリップボード監視と文字変換を実行します。</summary>
/// <remarks>
/// 提供機能: <br/>
/// - クリップボードの内容変更監視と自動文字変換（クリップボード変換が有効な場合のみ）<br/>
/// - 変換前テキストへの手動入力に対する変換<br/>
/// - 変換結果のクリップボードへの書き戻し<br/><br/>
/// 特徴: <br/>
/// - ITextConverter / IClipboardService / AppSetting に依存（DIP）<br/>
/// - クリップボード変換が無効な場合はクリップボードの読み取り・書き戻しを一切行いません<br/>
/// - 全角/半角変換は常に有効で、クリップボード変換の有効/無効とは独立です<br/>
/// - 書き戻し中の再帰イベントは _isUpdatingClipboard フラグで防止します
/// </remarks>
public partial class HomeViewModel : ObservableObject, IDisposable
{
    /// <summary>ロガー（バックグラウンドの変換は画面に出ないため、記録で追えるようにする）。</summary>
    private static readonly ILogger s_logger = AppLog.CreateLogger(nameof(HomeViewModel));

    /// <summary>テキスト変換器（DIP: ITextConverter）。</summary>
    private readonly ITextConverter _converter;
    /// <summary>クリップボードサービス（DIP: IClipboardService）。</summary>
    private readonly IClipboardService _clipboardService;
    /// <summary>アプリ設定。クリップボード変換の有効/無効を参照します。</summary>
    private readonly AppSetting _appSetting;
    /// <summary>クリップボード書き戻し中フラグ（再帰防止）。</summary>
    private bool _isUpdatingClipboard;
    /// <summary>Dispose 済みフラグ。</summary>
    private bool _disposed;

    /// <summary>変換前のテキストを取得または設定します。</summary>
    /// <remarks>クリップボードからの取り込み、または変換前テキストボックスの編集で更新されます。<br/>
    /// 変更時は自動的に変換され、ConvertedText へ反映されます。</remarks>
    [ObservableProperty]
    public partial string BeforeText { get; set; } = "";

    /// <summary>変換後のテキストを取得または設定します。</summary>
    [ObservableProperty]
    public partial string ConvertedText { get; set; } = "";

    /// <summary>HomeViewModel の新しいインスタンスを初期化します。</summary>
    /// <param name="converter">テキスト変換器。</param>
    /// <param name="clipboardService">クリップボードサービス。</param>
    /// <param name="appSetting">アプリ設定。クリップボード変換の有効/無効を参照します。</param>
    public HomeViewModel(ITextConverter converter, IClipboardService clipboardService, AppSetting appSetting)
    {
        _converter = converter;
        _clipboardService = clipboardService;
        _appSetting = appSetting;
        _clipboardService.ContentChanged += Clipboard_ContentChanged;
    }

    /// <summary>クリップボードのイベント購読を解除します。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _clipboardService.ContentChanged -= Clipboard_ContentChanged;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>変換前テキストの変更時に呼び出され、変換後テキストを更新します。</summary>
    /// <param name="value">新しい変換前テキスト。</param>
    /// <remarks>クリップボード書き戻し中は再入を避けるため何もしません。<br/>
    /// 変更の要因（クリップボード取り込み・テキストボックスの編集）に関わらず変換を実行します。</remarks>
    partial void OnBeforeTextChanged(string value)
    {
        if (_isUpdatingClipboard) return;
        ConvertedText = ConvertForDisplay(value);
    }

    /// <summary>クリップボードの内容変更時に呼び出され、変換処理を開始します。</summary>
    /// <param name="sender">イベントソース。</param>
    /// <param name="e">イベントデータ。</param>
    /// <remarks>イベント ハンドラーから非同期の処理を開始します（`async void` は使いません。
    /// 失敗は <see cref="RunClipboardChangeAsync"/> が受け止めます）。</remarks>
    private void Clipboard_ContentChanged(object? sender, EventArgs e) => _ = RunClipboardChangeAsync();

    /// <summary>クリップボード変更の処理を実行し、想定内の失敗を握ります。</summary>
    /// <returns>処理の完了を表すタスク。</returns>
    private async Task RunClipboardChangeAsync()
    {
        try
        {
            await HandleClipboardChangeAsync();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            // バックグラウンド時などクリップボードアクセスが拒否されるケースは想定内だが、
            // 握りつぶさず記録する（「変換されない」をログから追えるようにする）
            s_logger.LogClipboardAccessFailed(ex, "読み取りまたは書き戻し");
        }
    }

    /// <summary>クリップボード変更を処理します。</summary>
    /// <remarks>
    /// 処理フロー: <br/>
    /// 1. クリップボード変換が無効、または書き戻し中の場合はスキップ<br/>
    /// 2. クリップボードからテキスト取得<br/>
    /// 3. 変換後テキストと同一ならスキップ（書き戻し済みの値を再処理しない）<br/>
    /// 4. 変換前テキストを更新（変換後テキストは自動更新）<br/>
    /// 5. 変換結果が変化していればクリップボードへ書き戻し<br/><br/>
    /// 注意点: <br/>
    /// - クリップボード変換が無効な場合、読み取りも書き戻しも行いません
    /// </remarks>
    private async Task HandleClipboardChangeAsync()
    {
        if (_isUpdatingClipboard || !_appSetting.IsClipboardConvertEnabled)
        {
            s_logger.LogClipboardConversionSkipped("変換が無効、または書き戻し中");
            return;
        }

        var text = await _clipboardService.GetTextAsync();
        if (string.IsNullOrEmpty(text))
        {
            s_logger.LogClipboardConversionSkipped("テキストが空");
            return;
        }

        // 内容は記録しない（機密が含まれ得るため、文字数だけで切り分ける）
        s_logger.LogClipboardChanged(text.Length);

        if (string.Equals(ConvertedText, text, StringComparison.Ordinal))
        {
            s_logger.LogClipboardConversionSkipped("表示中の変換結果と同じ");
            return;
        }

        // await 中に無効化された場合は、読み取り結果を破棄してクリップボードに触れない。
        if (!_appSetting.IsClipboardConvertEnabled)
        {
            s_logger.LogClipboardConversionSkipped("読み取り中に変換が無効化された");
            return;
        }

        BeforeText = text;
        if (string.Equals(ConvertedText, HomeDisplayText.NoChange, StringComparison.Ordinal))
        {
            s_logger.LogClipboardConversionSkipped("変換しても内容が変わらない");
            return;
        }

        try
        {
            _isUpdatingClipboard = true;
            _clipboardService.SetText(ConvertedText);
            _clipboardService.Flush();
            s_logger.LogClipboardConverted(text.Length, ConvertedText.Length);
        }
        finally
        {
            _isUpdatingClipboard = false;
        }
    }

    /// <summary>変換前テキストを表示用の変換後テキストへ変換します。</summary>
    /// <param name="text">変換前テキスト。</param>
    /// <returns>変換後テキスト。変化がない場合は「変換不要」の文言。</returns>
    private string ConvertForDisplay(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        var converted = _converter.Convert(text);
        return string.Equals(text, converted, StringComparison.Ordinal) ? HomeDisplayText.NoChange : converted;
    }
}
