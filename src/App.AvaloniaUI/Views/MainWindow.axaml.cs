using Avalonia;
using Avalonia.Controls;
using EsUtil.ZenHanConverter.App.AvaloniaUI.Services;
using EsUtil.ZenHanConverter.App.AvaloniaUI.ViewModels;
using EsUtil.Platform.Core.Geometry;
using EsUtil.ZenHanConverter.App.Core.Helpers;
using EsUtil.ZenHanConverter.App.Core.Models;
using EsUtil.ZenHanConverter.App.Core.Native;
using System;

namespace EsUtil.ZenHanConverter.App.AvaloniaUI.Views;

/// <summary>アプリケーションのメインウィンドウを表します。</summary>
/// <remarks>
/// 提供機能: <br/>
/// - カスタムタイトルバー（アプリ名・クリップボード変換スイッチ・プリセット選択・ウィンドウ操作ボタン）<br/>
/// - ナビゲーションペイン（ホーム / 設定）とコンテンツ表示<br/>
/// - ウィンドウサイズ・位置の復元と保存（表示領域外の補正を含む）<br/><br/>
/// 特徴: <br/>
/// - 装飾は <c>WindowDecorations="None"</c> としてアプリが描画し、タイトルバー領域には
///   <c>WindowDecorationProperties.ElementRole="TitleBar"</c> を指定してドラッグ移動とダブルクリック最大化を有効化<br/>
/// - クリップボード変換スイッチは <see cref="AppSetting.IsClipboardConvertEnabled"/> を単一所有し、
///   プリセット選択は <c>SettingsViewModel.SelectedPresetName</c> を単一所有元として設定画面と同期<br/>
/// - 保存位置が表示領域外の場合は Core の <see cref="WindowPlacement"/> で見える位置へ補正<br/><br/>
/// 注意点: <br/>
/// - 最大化中に終了した場合は、通常状態での最後の位置とサイズを保存します（最大化状態を次回起動へ引き継がないため）
/// </remarks>
public partial class MainWindow : Window
{
    /// <summary>ナビゲーションペインの幅（展開時、DIP）。</summary>
    private const double NavPaneExpandedWidth = 104;

    /// <summary>ナビゲーションペインの幅（折りたたみ時、アイコンのみ、DIP）。</summary>
    private const double NavPaneCompactWidth = 44;

    /// <summary>メインウィンドウの ViewModel を取得します。</summary>
    public MainWindowViewModel ViewModel { get; }

    /// <summary>アプリケーション設定を取得します。ウィンドウサイズ・位置の保存に使用します。</summary>
    public AppSetting AppSetting { get; }

    /// <summary>通常状態での最後のクライアントサイズ（DIP）。</summary>
    private Size _lastNormalClientSize;

    /// <summary>通常状態での最後のウィンドウ位置（物理ピクセル）。</summary>
    /// <remarks>Avalonia の Window.Position プロパティはドラッグ等の外部起点の移動を反映しないため、
    /// PositionChanged で追跡した値を保存時の単一の情報源とします。</remarks>
    private PixelPoint _lastNormalPosition;

    /// <summary>最後に取得できた DPI スケール。</summary>
    /// <remarks>Closed 時点では RenderScaling を取得できない（1.0 になる）ため、変化時に保持した値を保存に使用します。</remarks>
    private double _lastScale = 1.0;

    /// <summary>ナビゲーションペインが展開されているかどうか。</summary>
    private bool _isNavPaneExpanded = true;

    /// <summary>MainWindow の新しいインスタンスを初期化します。</summary>
    /// <param name="viewModel">メインウィンドウの ViewModel。</param>
    /// <param name="appSetting">ウィンドウサイズ・位置を保存する AppSetting。</param>
    /// <param name="navigation">画面解決を行うナビゲーションサービス。</param>
    public MainWindow(MainWindowViewModel viewModel, AppSetting appSetting, INavigationService navigation)
    {
        InitializeComponent();

        ViewModel = viewModel;
        AppSetting = appSetting;
        DataContext = viewModel;

        // ナビゲーション ペインの下端に UI 名とアプリの版を 2 行で出す（書式は AppVersion が単一所有する）
        VersionText.Text = AppVersion.For("AvaloniaUI");

        // 保存済みのサイズを初期値として適用（位置は表示後に適用する）
        Width = appSetting.WindowWidth;
        Height = appSetting.WindowHeight;
        _lastNormalClientSize = new Size(appSetting.WindowWidth, appSetting.WindowHeight);
        _lastNormalPosition = new PixelPoint(0, 0);
        WindowStartupLocation = appSetting.WindowX is null || appSetting.WindowY is null
            ? WindowStartupLocation.CenterScreen
            : WindowStartupLocation.Manual;

        PropertyChanged += OnWindowPropertyChanged;
        Opened += OnOpenedRestoreWindowPosition;
        ScalingChanged += (_, _) => UpdateScale();
        SizeChanged += (_, _) => TrackNormalBounds();
        PositionChanged += (_, _) => TrackNormalBounds();
        Closed += OnClosedSaveWindow;
        UpdateScale();

        // 初期画面（ホーム）を選択し、設定画面は UI スレッドのアイドル時に事前生成する
        ViewModel.SelectedNavItem = ViewModel.NavItems[0];
        navigation.PreloadView("Settings");
    }

    /// <summary>ウィンドウのプロパティ変化時に、最大化ボタンの表示と通常状態の位置・サイズの記録を更新します。</summary>
    /// <param name="sender">イベントソース。</param>
    /// <param name="e">変更されたプロパティを含むイベントデータ。</param>
    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowStateProperty)
            UpdateMaximizeGlyph();
    }

    /// <summary>最大化/復元ボタンの表示を現在のウィンドウ状態へ合わせます。</summary>
    private void UpdateMaximizeGlyph()
        => MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";

    /// <summary>通常状態での位置とサイズを記録します。</summary>
    /// <remarks>最大化/最小化中の値は通常状態へ戻ったときの値ではないため記録しません。</remarks>
    private void TrackNormalBounds()
    {
        if (WindowState != WindowState.Normal) return;
        _lastNormalClientSize = ClientSize;
        _lastNormalPosition = Position;
    }

    /// <summary>表示直後に保存されたウィンドウ位置を復元します。</summary>
    /// <param name="sender">イベントソース。</param>
    /// <param name="e">イベントデータ。</param>
    /// <remarks>処理フロー: <br/>
    /// 1. 保存位置が無い場合は何もしない（起動位置の指定に従う）<br/>
    /// 2. 保存位置を表示領域内へ補正<br/>
    /// 3. 物理ピクセルへ換算して適用</remarks>
    private void OnOpenedRestoreWindowPosition(object? sender, EventArgs e)
    {
        Opened -= OnOpenedRestoreWindowPosition;
        UpdateScale();

        if (AppSetting.WindowX is not double savedX || AppSetting.WindowY is not double savedY)
            return;

        var scale = CurrentScale();
        var target = ScreenVisibleArea.Clamp(
            new EsPoint(savedX, savedY), new EsSize(ClientSize.Width, ClientSize.Height), scale);
        Position = new PixelPoint(
            ScreenVisibleArea.ToPhysical(target.X, scale),
            ScreenVisibleArea.ToPhysical(target.Y, scale));
        _lastNormalPosition = Position;
    }

    /// <summary>ウィンドウクローズ時に現在のサイズと位置を保存します。</summary>
    /// <param name="sender">イベントソース。</param>
    /// <param name="e">イベントデータ。</param>
    /// <remarks>サイズと位置は DIP へ換算して保存します。<br/>
    /// デバウンス（300ms）待ちではプロセス終了に間に合わないため、同期で保存します。</remarks>
    private void OnClosedSaveWindow(object? sender, EventArgs e)
    {
        var isNormal = WindowState == WindowState.Normal;
        var size = isNormal ? ClientSize : _lastNormalClientSize;
        var position = _lastNormalPosition;
        var scale = CurrentScale();

        AppSetting.WindowWidth = size.Width;
        AppSetting.WindowHeight = size.Height;
        AppSetting.WindowX = ScreenVisibleArea.ToDip(position.X, scale);
        AppSetting.WindowY = ScreenVisibleArea.ToDip(position.Y, scale);

        AppSetting.SaveNow();
        ViewModel.Settings.ConvertConfig.SaveNow();
    }

    /// <summary>現在の DPI スケールを取得します。取得できない場合は保持している値、それも無ければ 1.0 を返します。</summary>
    /// <returns>DIP から物理ピクセルへの換算に使用するスケール。</returns>
    private double CurrentScale() => _lastScale > 0 ? _lastScale : 1.0;

    /// <summary>現在の RenderScaling を保持します（有効な値のときのみ更新）。</summary>
    private void UpdateScale()
    {
        var scaling = RenderScaling;
        if (scaling > 0)
            _lastScale = scaling;
    }

    /// <summary>ナビゲーションペインの幅（展開/折りたたみ）を切り替えます。</summary>
    /// <param name="sender">イベントソース。</param>
    /// <param name="e">イベントデータ。</param>
    /// <remarks>折りたたみ時はペインを消さずに幅を狭め、アイコンのみを表示します。</remarks>
    private void OnPaneToggleClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _isNavPaneExpanded = !_isNavPaneExpanded;
        NavPane.Width = _isNavPaneExpanded ? NavPaneExpandedWidth : NavPaneCompactWidth;
        NavList.Classes.Set("compact", !_isNavPaneExpanded);
        // 折りたたみ時は 2 行の版表示が入らないため隠す（アイコンだけの幅に収める）
        VersionText.IsVisible = _isNavPaneExpanded;
    }

    /// <summary>ウィンドウを最小化します。</summary>
    /// <param name="sender">イベントソース。</param>
    /// <param name="e">イベントデータ。</param>
    private void OnMinimizeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    /// <summary>ウィンドウを最大化または復元します。</summary>
    /// <param name="sender">イベントソース。</param>
    /// <param name="e">イベントデータ。</param>
    private void OnMaximizeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    /// <summary>ウィンドウを閉じます。</summary>
    /// <param name="sender">イベントソース。</param>
    /// <param name="e">イベントデータ。</param>
    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();
}
