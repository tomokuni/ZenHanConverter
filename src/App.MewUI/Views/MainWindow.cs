using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using EsUtil.ZenHanConverter.App.MewUI.Helpers;
using EsUtil.ZenHanConverter.App.MewUI.Services;
using EsUtil.Platform.Core.Geometry;
using EsUtil.ZenHanConverter.App.Core.Models;
using EsUtil.ZenHanConverter.App.Core.Helpers;
using EsUtil.ZenHanConverter.App.Core.Native;
using EsUtil.ZenHanConverter.App.Core.Presentation.ViewModels;

namespace EsUtil.ZenHanConverter.App.MewUI.Views;

/// <summary>アプリケーションのメインウィンドウを表します。</summary>
/// <remarks>
/// 提供機能: <br/>
/// - ウィンドウサイズの AppSetting からの復元 / クローズ時の保存<br/>
/// - カスタムタイトルバー（アプリ名・クリップボード変換スイッチ・プリセット選択・ウィンドウ操作ボタン・ドラッグ移動）<br/>
/// - NavigationView によるペイン式ナビゲーションとコンテンツ表示<br/>
/// 特徴: <br/>
/// - タイトルバーは ExtendClientAreaTitleBarHeight でクライアント領域へ拡張した独自領域<br/>
/// - ウィンドウ余白（Window 既定スタイルの ContainerPadding）は 0 にし、タイトルバーとウィンドウ操作ボタンをウィンドウ端へ寄せる<br/>
/// - タイトルバーの高さは操作系コントロールが縮小・クリップされない下限に合わせて詰める<br/>
/// - クリップボード変換スイッチはタイトルバーが単一所有（AppSetting.IsClipboardConvertEnabled にバインド）<br/>
/// - プリセット選択ドロップダウンは設定画面と同一仕様（SettingsViewModel を単一所有元とする）<br/>
/// - ウィンドウ操作ボタン（最小化/最大化/復元/閉じる）はネイティブのキャプションボタンを持たない環境向けに自前で描画<br/>
/// - タイトルバー（アプリ名・余白）はドラッグでウィンドウ移動、ダブルクリックで最大化ボタンと同じ切り替え<br/>
/// - NavigationView のペイン項目アイコンは FluentIcons（Core の SVG パスデータ）から取得<br/>
/// - NavigationView.ContentSelector で選択項目からビュー（HomeView / SettingsView）を解決<br/>
/// - NavigationService がビュー生成とキャッシュを担当
/// </remarks>
public sealed class MainWindow : Window
{
    /// <summary>タイトルバーに表示するアプリケーション名。</summary>
    private const string WindowTitle = "ZenHanConverter";

    /// <summary>カスタムタイトルバーの高さ（DIP）。操作系コントロール（スイッチ/コンボボックス）の標準高さ 28 を下回らない値とする。</summary>
    private const double TitleBarHeight = 32;

    /// <summary>タイトルバー左端のパディング（DIP）。</summary>
    private const double TitleBarPadding = 12;

    /// <summary>クリップボード変換スイッチとそのラベルの間隔（DIP）。</summary>
    private const double SwitchLabelSpacing = 4;

    /// <summary>クリップボード変換スイッチの上下パディング（DIP）。既定スタイルと同値を保ち、左右のみ詰めるために指定します。</summary>
    private const double SwitchVerticalPadding = 4;

    /// <summary>プリセット選択ドロップダウンの上余白（DIP）。タイトルバー高さを詰めた分、上端へ張り付かないよう確保します。</summary>
    private const double PresetDropDownTopMargin = 4;

    /// <summary>ウィンドウ操作ボタンの幅（DIP）。</summary>
    private const double WindowButtonWidth = 46;

    /// <summary>ウィンドウ操作ボタンのアイコン半径（DIP）。</summary>
    private const double WindowButtonGlyphSize = 5;

    /// <summary>最小化・最大化ボタンのスタイル名。</summary>
    private const string ChromeButtonStyleName = "TitleBarButton";

    /// <summary>閉じるボタンのスタイル名。</summary>
    private const string CloseButtonStyleName = "TitleBarCloseButton";

    /// <summary>閉じるボタンのホバー背景色。</summary>
    private static readonly Color s_closeButtonHotBackground = Color.FromRgb(232, 17, 35);

    /// <summary>閉じるボタンの押下背景色。</summary>
    private static readonly Color s_closeButtonPressedBackground = Color.FromRgb(200, 12, 28);

    /// <summary>ナビゲーション項目（タグと表示テキスト）。</summary>
    /// <param name="Tag">コンテンツ解決に使用するタグ（"Home" / "Settings"）。</param>
    /// <param name="Text">ペインに表示するテキスト。</param>
    /// <param name="Icon">ペインに表示するアイコン形状。</param>
    private sealed record NavItem(string Tag, string Text, PathGeometry Icon);

    /// <summary>最大化ボタンに表示するアイコン要素。ウィンドウ状態に応じて形状を切り替えます。</summary>
    private readonly GlyphElement _maximizeGlyph = new GlyphElement()
        .Kind(GlyphKind.WindowMaximize)
        .GlyphSize(WindowButtonGlyphSize);

    /// <summary>ナビゲーションの対象ビューを生成・解決するサービス。</summary>
    private readonly NavigationService _navigation;

    /// <summary>設定画面の ViewModel。プリセット選択の単一所有元。</summary>
    private readonly SettingsViewModel _settingsViewModel;

    /// <summary>ウィンドウ設定。クリップボード変換の有効/無効の単一所有元。</summary>
    private readonly AppSetting _appSetting;

    /// <summary>タイトルバーのスイッチにバインドするクリップボード変換の有効状態。</summary>
    private readonly ObservableValue<bool> _clipboardConvertEnabled;

    /// <summary>通常状態での最後のクライアントサイズ。最大化/最小化中の終了時に保存するサイズ。</summary>
    /// <remarks>Window.RestoreBounds は OS 起点の最大化では最大化後のサイズを返すため、通常状態のサイズを自前で追跡します。</remarks>
    private Size _lastNormalClientSize;

    /// <summary>通常状態での最後のウィンドウ位置（画面座標・DIP）。最大化/最小化中の終了時に保存する位置。</summary>
    /// <remarks>位置の変更イベントが無いため、移動操作の完了時とウィンドウ状態の変化時に記録します。</remarks>
    private Point _lastNormalPosition;

    /// <summary>MainWindow の新しいインスタンスを初期化します。</summary>
    /// <param name="appSetting">ウィンドウ設定。タイトルバーのスイッチが IsClipboardConvertEnabled を読み書きします。</param>
    /// <param name="navigation">ナビゲーションサービス。</param>
    /// <param name="settingsViewModel">設定画面の ViewModel。タイトルバーのプリセット選択が参照します。</param>
    public MainWindow(AppSetting appSetting, NavigationService navigation, SettingsViewModel settingsViewModel)
    {
        _appSetting = appSetting;
        _navigation = navigation;
        _settingsViewModel = settingsViewModel;
        _clipboardConvertEnabled = new ObservableValue<bool>(appSetting.IsClipboardConvertEnabled, v => v);
        _lastNormalClientSize = new Size(appSetting.WindowWidth, appSetting.WindowHeight);
        _lastNormalPosition = new Point(appSetting.WindowX ?? 0, appSetting.WindowY ?? 0);

        // 外部で IsClipboardConvertEnabled が変更された場合もタイトルバーのスイッチへ反映
        appSetting.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppSetting.IsClipboardConvertEnabled) or "")
                _clipboardConvertEnabled.Value = appSetting.IsClipboardConvertEnabled;
        };

        this.Title(WindowTitle)
            .ExtendClientAreaTitleBarHeight(TitleBarHeight)
            .Resizable(_appSetting.WindowWidth, _appSetting.WindowHeight, minWidth: 720, minHeight: 480);

        // 保存位置を自前で適用するため、保存がある場合はバックエンド既定の初期配置を行わせない
        StartupLocation = _appSetting.WindowX is null || _appSetting.WindowY is null
            ? WindowStartupLocation.CenterScreen
            : WindowStartupLocation.Manual;

        // Window 既定スタイルの ContainerPadding(8) を打ち消し、タイトルバーとウィンドウ操作ボタンをウィンドウ端まで届かせる
        Padding = Thickness.Zero;

        // Window へ代入した時点でスタイルシートは凍結されるため、スタイル定義後に代入する
        StyleSheet = new StyleSheet()
            .With(ChromeButtonStyleName, CreateChromeButtonStyle)
            .With(CloseButtonStyleName, CreateCloseButtonStyle);

        // 最大化ボタンはウィンドウ状態（最大化/通常）に応じてアイコンを切り替える
        WindowStateChanged += OnWindowStateChangedForSave;

        // 表示直後に保存位置を復元し、終了時にウィンドウ位置・サイズと設定を保存する
        Loaded += OnLoadedRestoreWindowPosition;
        ClientSizeChanged += OnClientSizeChangedForSave;
        Closed += OnClosedSaveSettings;

        Global.MainWindow = this;
    }

    /// <summary>ウィンドウ状態の変化時に、通常状態の位置とサイズを記録します。</summary>
    /// <param name="state">新しいウィンドウ状態。</param>
    /// <remarks>最大/最小化中はウィンドウ位置が画面端や退避位置を指すため、通常状態の値のみを保存対象として記録します。</remarks>
    private void OnWindowStateChangedForSave(WindowState state)
    {
        UpdateMaximizeGlyph();

        if (state == WindowState.Normal)
        {
            _lastNormalClientSize = ClientSize;
            _lastNormalPosition = Position;
        }
    }

    /// <summary>表示直後に保存されたウィンドウ位置を復元します。</summary>
    /// <remarks>処理フロー: <br/>
    /// 1. 保存位置が無い場合は何もしない（バックエンド既定の配置を維持）<br/>
    /// 2. 仮想画面（全モニタの外接矩形）を物理ピクセルから DIP へ換算<br/>
    /// 3. 保存位置を表示領域内へ補正して適用<br/><br/>
    /// 注意点: <br/>
    /// - Loaded は初回描画より前に発火するため、最初のフレームから補正後の位置で表示されます</remarks>
    private void OnLoadedRestoreWindowPosition()
    {
        Loaded -= OnLoadedRestoreWindowPosition;

        if (_appSetting.WindowX is not double savedX || _appSetting.WindowY is not double savedY)
            return;

        var clamped = ScreenVisibleArea.Clamp(
            new EsPoint(savedX, savedY), new EsSize(ClientSize.Width, ClientSize.Height), DpiScale);

        Position = new Point(clamped.X, clamped.Y);
    }

    /// <summary>通常状態でのクライアントサイズを記録します。</summary>
    /// <param name="size">新しいクライアントサイズ。</param>
    /// <remarks>最大化/最小化中のサイズは、通常状態へ戻ったときの値ではないため記録しません。</remarks>
    private void OnClientSizeChangedForSave(Size size)
    {
        if (WindowState == WindowState.Normal)
            _lastNormalClientSize = size;
    }

    /// <summary>終了時にウィンドウの位置・サイズと各設定を保存します。</summary>
    /// <remarks>処理フロー: <br/>
    /// 1. 通常状態では現在の位置・クライアントサイズ、最大化/最小化中は通常状態での最後の値を採用<br/>
    /// 2. 自動保存のデバウンスを待たず、アプリ設定と変換設定を同期で保存<br/><br/>
    /// 注意点: <br/>
    /// - デバウンス（300ms）待ちではプロセス終了に間に合わず、直前の変更が失われるため、ここでは同期保存を行います<br/>
    /// - 最大化中の位置・サイズを保存すると次回起動が最大化状態を引き継いでしまうため、通常状態の値を保存します</remarks>
    private void OnClosedSaveSettings()
    {
        var isNormal = WindowState == WindowState.Normal;
        var size = isNormal ? ClientSize : _lastNormalClientSize;
        var position = isNormal ? Position : _lastNormalPosition;

        _appSetting.WindowWidth = size.Width;
        _appSetting.WindowHeight = size.Height;
        _appSetting.WindowX = position.X;
        _appSetting.WindowY = position.Y;

        _appSetting.SaveNow();
        _settingsViewModel.ConvertConfig.SaveNow();
    }

    /// <summary>ウィンドウの内容を構築します。</summary>
    protected override void OnBuild() =>
        this.Content(
            new Grid()
                .Rows("Auto,*")
                .Children(
                    BuildTitleBar().Row(0),
                    BuildNavigationView().Row(1)
                )
        );

    /// <summary>カスタムタイトルバー（アプリ名 + クリップボード変換スイッチ + プリセット選択 + ウィンドウ操作ボタン）を構築します。</summary>
    /// <returns>タイトルバーの要素。</returns>
    /// <remarks>
    /// 処理フロー: <br/>
    /// 1. 左にアプリ名、中央寄りにクリップボード変換スイッチ、さらにプリセット選択を配置<br/>
    /// 2. 右端にウィンドウ操作ボタン（最小化/最大化/閉じる）を配置<br/>
    /// 3. アプリ名と右側余白のマウスダウンでウィンドウをドラッグ移動、ダブルクリックで最大化/復元<br/>
    /// 4. スイッチ・プリセット・ウィンドウ操作ボタンはドラッグ・ダブルクリックに巻き込まないよう、それらの領域には設定しない<br/><br/>
    /// 注意点: <br/>
    /// - クリップボード変換スイッチの有効/無効はプリセット選択の有効/無効に影響しません
    /// </remarks>
    private Element BuildTitleBar()
    {
        var titleLabel = new Label()
            .Text(WindowTitle)
            .Bold()
            .CenterVertical()
            .OnMouseDown(OnTitleBarMouseDown)
            .OnMouseDoubleClick(OnTitleBarMouseDoubleClick);

        // スイッチとそのラベルは 1 つの操作単位として狭い間隔で並べる
        var convertToggle = new StackPanel()
            .Horizontal()
            .Spacing(SwitchLabelSpacing)
            .CenterVertical()
            .Children(
                new ToggleSwitch()
                    .BindIsChecked(_clipboardConvertEnabled)
                    .OnCheckedChanged(value => _appSetting.IsClipboardConvertEnabled = value)
                    // 既定スタイルはトラックの左右にもパディングを持ち、ラベルとの見た目の間隔を広げる。
                    // 左右のみ 0 にして、トラックとラベルを詰めて並べる。
                    .Padding(0, SwitchVerticalPadding, 0, SwitchVerticalPadding)
                    .CenterVertical(),
                new Label()
                    .Text("クリップボード変換")
                    .CenterVertical()
            );

        var controls = new StackPanel()
            .Column(1)
            .Horizontal()
            .Spacing(24)
            .Margin(24, 0, 0, 0)
            .Children(
                convertToggle,
                new ComboBox()
                    .Placeholder("プリセット未選択")
                    .Margin(0, PresetDropDownTopMargin, 0, 0)
                    .BindSelectedPreset(_settingsViewModel)
                    .CenterVertical()
            );

        // 右側の余白（ドラッグ領域。ウィンドウ操作ボタンの左隣に位置する）
        var dragArea = new Border()
            .Column(2)
            .OnMouseDown(OnTitleBarMouseDown)
            .OnMouseDoubleClick(OnTitleBarMouseDoubleClick);

        // 右端パディングはウィンドウ操作ボタンを縁へ寄せるため 0 とする
        return new Border()
            .Padding(TitleBarPadding, 0, 0, 0)
            .Child(
                new Grid()
                    .Columns("Auto,Auto,*,Auto")
                    .Children(titleLabel, controls, dragArea, BuildWindowButtons())
            );
    }

    /// <summary>タイトルバーのウィンドウ操作ボタン（最小化・最大化/復元・閉じる）を構築します。</summary>
    /// <returns>ウィンドウ操作ボタンを左から並べた要素。</returns>
    /// <remarks>
    /// 処理フロー: <br/>
    /// 1. 最小化・最大化（復元）・閉じるの順にボタンを生成<br/>
    /// 2. 各ボタンは Window の Minimize / Maximize / Restore / Close を呼び出す<br/><br/>
    /// 注意点: <br/>
    /// - Win32 バックエンドはネイティブのキャプションボタンを提供しないため、常に表示します
    /// </remarks>
    private Element BuildWindowButtons() =>
        new StackPanel()
            .Column(3)
            .Horizontal()
            .Children(
                CreateWindowButton(new GlyphElement().Kind(GlyphKind.WindowMinimize).GlyphSize(WindowButtonGlyphSize))
                    .OnClick(Minimize),
                CreateWindowButton(_maximizeGlyph)
                    .OnClick(ToggleMaximize),
                CreateWindowButton(new GlyphElement().Kind(GlyphKind.Cross).GlyphSize(WindowButtonGlyphSize), isClose: true)
                    .OnClick(Close)
            );

    /// <summary>タイトルバーのウィンドウ操作ボタンを生成します。</summary>
    /// <param name="content">ボタンに表示するアイコン要素。</param>
    /// <param name="isClose">閉じるボタン（赤系のホバー配色）にするかどうか。</param>
    /// <returns>スタイル適用済みのウィンドウ操作ボタン。</returns>
    private static Button CreateWindowButton(Element content, bool isClose = false) =>
        new Button()
        {
            Content = content,
            MinWidth = WindowButtonWidth,
            MinHeight = TitleBarHeight,
            StyleName = isClose ? CloseButtonStyleName : ChromeButtonStyleName,
        };

    /// <summary>最小化・最大化ボタンのスタイルを生成します。</summary>
    /// <returns>ホバー/押下で背景色が変化するタイトルバー用ボタンスタイル。</returns>
    private static Style CreateChromeButtonStyle() =>
        new(typeof(Button))
        {
            Transitions = [Transition.Create(Control.BackgroundProperty)],
            Setters =
            [
                Setter.Create(Control.BackgroundProperty, Color.Transparent),
                Setter.Create(Control.BorderThicknessProperty, 0.0),
                Setter.Create(Control.CornerRadiusProperty, 0.0),
                Setter.Create(Control.PaddingProperty, new Thickness(0)),
            ],
            Triggers =
            [
                new StateTrigger
                {
                    Match = VisualStateFlags.Hot,
                    Setters = [Setter.Create(Control.BackgroundProperty, theme => theme.Palette.ButtonFace)],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.Pressed,
                    Setters = [Setter.Create(Control.BackgroundProperty, theme => theme.Palette.ButtonPressedBackground)],
                },
            ],
        };

    /// <summary>閉じるボタンのスタイルを生成します。</summary>
    /// <returns>ホバー/押下で赤系の背景色に変化するタイトルバー用ボタンスタイル。</returns>
    private static Style CreateCloseButtonStyle() =>
        new(typeof(Button))
        {
            Transitions =
            [
                Transition.Create(Control.BackgroundProperty),
                Transition.Create(TextElement.ForegroundProperty),
            ],
            Setters =
            [
                Setter.Create(Control.BackgroundProperty, Color.Transparent),
                Setter.Create(Control.BorderThicknessProperty, 0.0),
                Setter.Create(Control.CornerRadiusProperty, 0.0),
                Setter.Create(Control.PaddingProperty, new Thickness(0)),
            ],
            Triggers =
            [
                new StateTrigger
                {
                    Match = VisualStateFlags.Hot,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty, s_closeButtonHotBackground),
                        Setter.Create(TextElement.ForegroundProperty, Color.White),
                    ],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.Pressed,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty, s_closeButtonPressedBackground),
                        Setter.Create(TextElement.ForegroundProperty, Color.White),
                    ],
                },
            ],
        };

    /// <summary>ウィンドウを最大化し、すでに最大化中の場合は元のサイズへ復元します。</summary>
    private void ToggleMaximize()
    {
        if (WindowState == WindowState.Maximized)
        {
            Restore();
            return;
        }

        Maximize();
    }

    /// <summary>ウィンドウ状態に合わせて最大化ボタンのアイコン（最大化/復元）を切り替えます。</summary>
    private void UpdateMaximizeGlyph() =>
        _maximizeGlyph.Kind = WindowState == WindowState.Maximized ? GlyphKind.WindowRestore : GlyphKind.WindowMaximize;

    /// <summary>タイトルバーのマウスダウン時にウィンドウをドラッグ移動します。</summary>
    /// <param name="e">マウスイベントデータ。</param>
    /// <remarks>移動操作はネイティブの移動ループで完結するため、戻った時点の位置を通常状態の位置として記録します。</remarks>
    private void OnTitleBarMouseDown(MouseEventArgs e)
    {
        if (e.Handled || !e.LeftButton) return;
        DragMove();
        e.Handled = true;

        if (WindowState == WindowState.Normal)
            _lastNormalPosition = Position;
    }

    /// <summary>タイトルバーのダブルクリック時にウィンドウを最大化または復元します。</summary>
    /// <param name="e">マウスイベントデータ。</param>
    /// <remarks>最大化ボタンの押下と同じ動作（通常時は最大化、最大化中は復元）を行います。</remarks>
    private void OnTitleBarMouseDoubleClick(MouseEventArgs e)
    {
        if (e.Handled || !e.LeftButton) return;
        ToggleMaximize();
        e.Handled = true;
    }

    /// <summary>NavigationView（ペイン式ナビゲーション）を構築します。</summary>
    /// <returns>NavigationView 要素。</returns>
    /// <remarks>ペイン項目のアイコンは FluentIcons（Core の SVG パスデータ）から取得します。<br/>
    /// 配置は Inline を明示します（既定の Auto では利用可能幅が 1000 DIP 未満のとき Overlay へ切り替わり、
    /// ペインが内容の上に重なる形へ変化するため、ウィンドウを狭めてもペインを並べて表示し続けます）。</remarks>
    private Element BuildNavigationView()
    {
        var items = new[]
        {
            new NavItem("Home", "ホーム", FluentIcons.ConvertRange),
            new NavItem("Settings", "設定", FluentIcons.Settings),
        };

        var navigationView = new NavigationView()
            .Items(items, x => x.Text, icon: x => x.Icon, content: x => ResolveContentView(x.Tag));

        // ウィンドウ幅を狭めてもペインを内容の横へ並べたままにする（Auto の幅依存による切替を抑止）
        navigationView.PaneDisplayMode = PaneDisplayMode.Inline;
        navigationView.PaneWidth = 100;
        navigationView.SelectedIndex = 0;

        // ペインの下端に UI 名とアプリの版を 2 行で出す（選択できない Header 行として置く。
        // 行の文字列に改行を含めるため Header にする。 書式は AppVersion が単一所有する）
        var footerItems = new[] { new NavItem("Version", AppVersion.For("MewUI"), FluentIcons.Settings) };
        navigationView.FooterPane.Items(
            footerItems,
            x => x.Text,
            kind: _ => NavigationItemKind.Header,
            keySelector: x => x.Tag);

        return navigationView;
    }

    /// <summary>選択されたナビゲーション項目のタグに対応するビューを取得します。</summary>
    /// <param name="tag">ナビゲーションタグ（"Home" / "Settings"）。</param>
    /// <returns>タグに対応するビュー要素。</returns>
    /// <remarks>ビューの生成は NavigationService に委譲し、キャッシュされたインスタンスを再利用します。</remarks>
    private Element ResolveContentView(string tag) => _navigation.ResolveView(tag);
}
