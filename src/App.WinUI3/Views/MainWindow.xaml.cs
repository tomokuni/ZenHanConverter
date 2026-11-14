using EsUtil.ZenHanConverter.App.WinUI3.Helpers;
using EsUtil.ZenHanConverter.App.WinUI3.ViewModels;
using EsUtil.Platform.Core.Geometry;
using EsUtil.ZenHanConverter.App.Core.Helpers;
using EsUtil.ZenHanConverter.App.Core.Models;
using EsUtil.ZenHanConverter.App.Core.Native;
using EsUtil.ZenHanConverter.App.Core.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Linq;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace EsUtil.ZenHanConverter.App.WinUI3.Views;

/// <summary>アプリケーションのメインウィンドウを表します。</summary>
/// <remarks>NavigationView と TitleBar を備え、ページ遷移のコンテナとして機能します。<br/>
/// 特徴: <br/>
/// - アクティブ化時に AppSetting のウィンドウサイズと位置を復元<br/>
/// - クローズ時に現在のウィンドウサイズと位置を AppSetting に保存<br/>
/// - 保存位置が表示領域外の場合は Core の WindowPlacement で見える位置へ補正<br/>
/// - プリセット選択はタイトルバーが単一所有（SettingsViewModel.SelectedPresetName に同期）<br/>
/// - MicaBackdrop による高速な背景レンダリング<br/>
/// - タイトルバーの拡張（ExtendsContentIntoTitleBar）</remarks>
public sealed partial class MainWindow : Window
{
    /// <summary>メインウィンドウの ViewModel を取得します。</summary>
    public MainWindowViewModel ViewModel { get; }

    /// <summary>アプリケーション設定を取得します。ウィンドウサイズ・位置の保存に使用します。</summary>
    public AppSetting AppSetting { get; }

    /// <summary>設定画面の ViewModel。タイトルバーのプリセット選択の単一所有元。</summary>
    private readonly SettingsViewModel _settingsViewModel;

    /// <summary>プリセット選択の同期中フラグ（ComboBox と ViewModel の循環更新を防止）。</summary>
    private bool _isSyncingPreset;

    /// <summary>ページコンテンツを表示する Grid。XAML の x:Name="contentFrame" にバインド。</summary>
    public Grid ContentFrame => this.contentFrame;
    /// <summary>ナビゲーションメニュー。XAML の x:Name="navigationView" にバインド。</summary>
    public NavigationView NavigationView => navigationView;
    /// <summary>カスタムタイトルバー。XAML の x:Name="titleBar" にバインド。</summary>
    public TitleBar TitleBar => this.titleBar;

    /// <summary>MainWindow の新しいインスタンスを初期化します。</summary>
    /// <param name="viewModel">メインウィンドウの ViewModel</param>
    /// <param name="appSetting">ウィンドウサイズ・位置を保存する AppSetting</param>
    /// <param name="settingsViewModel">設定画面の ViewModel。タイトルバーのプリセット選択が参照します。</param>
    public MainWindow(MainWindowViewModel viewModel, AppSetting appSetting, SettingsViewModel settingsViewModel)
    {
        this.InitializeComponent();
        this.ViewModel = viewModel;
        this.AppSetting = appSetting;
        _settingsViewModel = settingsViewModel;

        ApplyNavigationIcons();
        BindPresetSelector();
        UpdateVersionText();

        // 折りたたみ時は 2 行の版表示が入らないため、ペインの開閉に追随して表示を切り替える
        navigationView.PaneOpened += (_, _) => VersionText.Visibility = Visibility.Visible;
        navigationView.PaneClosed += (_, _) => VersionText.Visibility = Visibility.Collapsed;

        // SettingsItem は NavigationView のテンプレート適用時に生成されるため、ロード後にも設定する
        this.RootGrid.Loaded += (_, _) => ApplyNavigationIcons();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(this.TitleBar);

        this.Activated += MainWindow_Activated;
        this.Closed += MainWindow_Closed;
    }

    /// <summary>タイトルバーのプリセット選択を設定画面の ViewModel と双方向に同期します。</summary>
    /// <remarks>SettingsViewModel.SelectedPresetName を単一所有元とし、設定画面のドロップダウンと同じ値を共有します。</remarks>
    private void BindPresetSelector()
    {
        PresetComboBox.ItemsSource = _settingsViewModel.PresetNames;
        SyncPresetSelection();

        // ComboBox → ViewModel（ユーザーによる選択）
        PresetComboBox.SelectionChanged += OnPresetSelectionChanged;

        // ViewModel → ComboBox（プリセット連動・一致検出など）
        _settingsViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SettingsViewModel.SelectedPresetName) or nameof(SettingsViewModel.PresetNames))
                SyncPresetSelection();
        };
    }

    /// <summary>プリセット選択の変更を ViewModel へ反映します。</summary>
    private void OnPresetSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingPreset) return;
        _settingsViewModel.SelectedPresetName = PresetComboBox.SelectedItem as string;
    }

    /// <summary>ViewModel の選択状態を ComboBox へ反映します。</summary>
    private void SyncPresetSelection()
    {
        _isSyncingPreset = true;
        try
        {
            var selected = _settingsViewModel.SelectedPresetName;
            PresetComboBox.SelectedItem = selected is null
                ? null
                : _settingsViewModel.PresetNames.FirstOrDefault(name => string.Equals(name, selected, System.StringComparison.Ordinal));
        }
        finally
        {
            _isSyncingPreset = false;
        }
    }

    /// <summary>ナビゲーションペインのアイコンを FluentIcons の形状へ設定します。</summary>
    /// <remarks>ペイン項目のアイコンは FluentIcons（Core の SVG パスデータ）から取得します。<br/>
    /// 設定項目は NavigationView が自動生成するため、SettingsItem を取得して設定します。</remarks>
    private void ApplyNavigationIcons()
    {
        HomeNavItem.Icon = new PathIcon { Data = FluentIcons.ConvertRange };

        if (navigationView.SettingsItem is NavigationViewItem settingsItem)
            settingsItem.Icon = new PathIcon { Data = FluentIcons.Settings };
    }

    /// <summary>ペイン下端の UI 名とアプリの版を設定します。</summary>
    /// <remarks>書式（2 行・<c>v</c> の付け方）は AppVersion が単一所有します。</remarks>
    private void UpdateVersionText() => VersionText.Text = AppVersion.For("WinUI3");

    /// <summary>ウィンドウアクティブ化時に AppSetting からサイズと位置を復元します。</summary>
    /// <remarks>処理フロー: <br/>
    /// 1. 保存されたサイズ（DIP）を物理ピクセルへ換算して適用<br/>
    /// 2. 保存位置が無い場合は何もしない（OS 既定の配置を維持）<br/>
    /// 3. 保存位置を表示領域内へ補正して適用</remarks>
    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        this.Activated -= MainWindow_Activated;
        if (AppWindow == null) return;

        double dpiScale = GetWindowDpiScale(this);
        AppWindow.Resize(new SizeInt32(
                (int)(AppSetting.WindowWidth * dpiScale),
                (int)(AppSetting.WindowHeight * dpiScale)));

        if (AppSetting.WindowX is not double savedX || AppSetting.WindowY is not double savedY)
            return;

        var target = ScreenVisibleArea.Clamp(
            new EsPoint(savedX, savedY), new EsSize(AppSetting.WindowWidth, AppSetting.WindowHeight), dpiScale);
        AppWindow.Move(new PointInt32((int)(target.X * dpiScale), (int)(target.Y * dpiScale)));
    }

    /// <summary>ウィンドウクローズ時に現在のサイズと位置を AppSetting に保存します。</summary>
    /// <remarks>サイズと位置は DIP へ換算して保存します。<br/>
    /// デバウンス待ちではプロセス終了に間に合わないため、同期で保存します。</remarks>
    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        if (AppWindow == null) return;

        double dpiScale = GetWindowDpiScale(this);
        AppSetting.WindowWidth = AppWindow.Size.Width / dpiScale;
        AppSetting.WindowHeight = AppWindow.Size.Height / dpiScale;
        AppSetting.WindowX = AppWindow.Position.X / dpiScale;
        AppSetting.WindowY = AppWindow.Position.Y / dpiScale;
        AppSetting.SaveToJsonFile(AppSetting.AutoSaveFileName);
    }

    /// <summary>保存されたウィンドウ位置を表示領域内へ補正します。</summary>
    /// <summary>タイトルバーのパネルトグルリクエストを処理します。</summary>
    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        this.NavigationView.IsPaneOpen = !this.NavigationView.IsPaneOpen;
    }

    /// <summary>指定されたウィンドウハンドルの DPI 値を取得します（Win32 API）。</summary>
    /// <param name="hwnd">ウィンドウハンドル</param>
    /// <returns>DPI 値（例: 96, 120, 144）</returns>
    // LibraryImport（ソース生成）を使う。 DllImport は SYSLIB1054（LibraryImport を使うこと）を出すため、
    // 抑止ではなく生成側へ寄せる（csproj の AllowUnsafeBlocks=true で生成コードを受け入れている）。
    [LibraryImport("User32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetDpiForWindow(nint hwnd);

    /// <summary>指定されたウィンドウの DPI スケールを取得します。</summary>
    /// <param name="window">DPI スケールを取得するウィンドウ</param>
    /// <returns>DPI スケール値（例: 1.0, 1.25, 1.5）</returns>
    public static double GetWindowDpiScale(Window window)
    {
        nint windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        if (windowHandle == 0) return 1.0;
        return GetDpiForWindow(windowHandle) / 96D;
    }
}
