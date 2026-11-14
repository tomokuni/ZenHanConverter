using EsUtil.ZenHanConverter.App.WinForms.Helpers;
using EsUtil.ZenHanConverter.App.WinForms.ViewModels;
using EsUtil.Platform.Core.Geometry;
using EsUtil.ZenHanConverter.App.Core.Helpers;
using EsUtil.ZenHanConverter.App.Core.Models;
using EsUtil.ZenHanConverter.App.Core.Native;
using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace EsUtil.ZenHanConverter.App.WinForms.Views;

/// <summary>アプリケーションのメインウィンドウを表します。</summary>
/// <remarks>
/// 提供機能: <br/>
/// - カスタムタイトルバー（アプリ名・クリップボード変換スイッチ・プリセット選択・ウィンドウ操作ボタン）<br/>
/// - ナビゲーションペイン（ホーム / 設定）とコンテンツ表示<br/>
/// - ウィンドウサイズ・位置の復元と保存（表示領域外の補正を含む）<br/>
/// - 枠・角のドラッグによるリサイズ、タイトルバーのドラッグによる移動とダブルクリックによる最大化<br/><br/>
/// 特徴: <br/>
/// - <see cref="FormBorderStyle.None"/> として装飾を自前で描画し、<c>WM_NCHITTEST</c> の応答で
///   移動（HTCAPTION）とリサイズ（HTLEFT 等）を OS に処理させる<br/>
/// - クリップボード変換スイッチは <see cref="AppSetting.IsClipboardConvertEnabled"/> を単一所有し、
///   プリセット選択は <see cref="MainWindowViewModel.Settings"/> を単一所有元として設定画面と同期<br/>
/// - 保存位置が表示領域外の場合は Core の <see cref="WindowPlacement"/> で見える位置へ補正<br/><br/>
/// 注意点: <br/>
/// - 最大化中に終了した場合は、通常状態での最後の位置とサイズを保存します（最大化状態を次回起動へ引き継がないため）
/// </remarks>
public sealed class MainForm : Form
{
    /// <summary>タイトルバーに表示するアプリケーション名。</summary>
    private const string WindowTitle = "ZenHanConverter";

    /// <summary>クリップボード変換スイッチのラベルとアクセシブル名。</summary>
    private const string ClipboardToggleLabelText = "クリップボード変換";

    /// <summary>ウィンドウの最小幅（論理ピクセル）。</summary>
    private const int MinimumWidth = 720;

    /// <summary>ウィンドウの最小高さ（論理ピクセル）。</summary>
    private const int MinimumHeight = 480;

    /// <summary>プリセット選択ドロップダウンの最小幅（論理ピクセル）。</summary>
    private const int PresetComboMinWidth = 120;

    /// <summary>プリセット選択ドロップダウンの最大幅（論理ピクセル）。</summary>
    private const int PresetComboMaxWidth = 300;

    /// <summary>プリセット選択ドロップダウンの文字幅に加える付加幅（矢印・余白、論理ピクセル）。</summary>
    private const int PresetComboChromeWidth = 32;
    /// <summary>ナビゲーション項目のアイコンサイズ（論理ピクセル）。</summary>
    private const int NavIconSize = 16;

    /// <summary>ナビゲーションの開閉ボタンに表示するグリフ。</summary>
    private const string NavToggleGlyph = "☰";

    /// <summary>最大化時に表示するグリフ。</summary>
    private const string MaximizeGlyph = "□";

    /// <summary>最大化中に表示する復元グリフ。</summary>
    private const string RestoreGlyph = "❐";

    /// <summary>折り返しの基準となる設計時のフォント寸法。</summary>
    /// <remarks><c>Form.AutoScaleDimensions</c> と名前が衝突しないよう別名にしています。</remarks>
    private static readonly SizeF s_designAutoScaleDimensions = new(7F, 15F);

    /// <summary>ウィンドウプロパティを設定するためのメッセージ。</summary>
    private const int WmNcHitTest = 0x0084;

    /// <summary>メインウィンドウの ViewModel を取得します。</summary>
    public MainWindowViewModel ViewModel { get; }

    /// <summary>アプリケーション設定を取得します。ウィンドウサイズ・位置とクリップボード変換の保存に使用します。</summary>
    public AppSetting AppSetting { get; }

    /// <summary>タイトルバーの領域。</summary>
    private readonly Panel _titleBar = new();

    /// <summary>アプリ名のラベル。</summary>
    private readonly Label _appNameLabel = new();

    /// <summary>クリップボード変換のスイッチ。</summary>
    private readonly ToggleSwitch _clipboardToggle = new();

    /// <summary>クリップボード変換スイッチの説明ラベル。</summary>
    private readonly Label _clipboardToggleLabel = new();

    /// <summary>プリセット選択のドロップダウン。</summary>
    private readonly PresetComboBox _presetCombo;

    /// <summary>最小化ボタン。</summary>
    private readonly Button _minimizeButton = new();

    /// <summary>最大化/復元ボタン。</summary>
    private readonly Button _maximizeButton = new();

    /// <summary>閉じるボタン。</summary>
    private readonly Button _closeButton = new();

    /// <summary>ナビゲーションペイン。</summary>
    private readonly ListBox _navList = new();

    /// <summary>ナビゲーションペインの下端に出す UI 名とアプリの版（2 行）。</summary>
    private readonly Label _navFooter = new();

    /// <summary>ナビゲーションペインの開閉ボタン。</summary>
    private readonly Button _navToggleButton = new();

    /// <summary>ナビゲーションペインが展開されているかどうか。</summary>
    private bool _isNavPaneExpanded = true;

    /// <summary>コンテンツ（ホーム / 設定）の表示領域。</summary>
    private readonly Panel _contentHost = new();

    /// <summary>通常状態での最後のクライアントサイズ（物理ピクセル）。</summary>
    private Size _lastNormalClientSize;

    /// <summary>通常状態での最後のウィンドウ位置（物理ピクセル）。</summary>
    private Point _lastNormalPosition;

    /// <summary>保存済みのウィンドウ位置・サイズを適用済みかどうか。</summary>
    private bool _isBoundsRestored;

    /// <summary>画面の表示を更新する処理中かどうか（イベントの再入と二重実行を防ぐ）。</summary>
    private bool _isUpdatingUi;

    /// <summary>MainForm の新しいインスタンスを初期化します。</summary>
    /// <param name="viewModel">メインウィンドウの ViewModel。</param>
    /// <param name="appSetting">ウィンドウサイズ・位置とクリップボード変換を保存する AppSetting。</param>
    public MainForm(MainWindowViewModel viewModel, AppSetting appSetting)
    {
        ViewModel = viewModel;
        AppSetting = appSetting;
        _presetCombo = new PresetComboBox(viewModel.Settings);

        Text = WindowTitle;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = appSetting.WindowX is null || appSetting.WindowY is null
            ? FormStartPosition.CenterScreen
            : FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Font;
        AutoScaleDimensions = s_designAutoScaleDimensions;
        BackColor = AppTheme.TitleBarBackground;

        BuildLayout();
        BindViewModel();

        Resize += OnResizeTrackNormalBounds;
        Move += OnMoveTrackNormalBounds;
        FormClosing += OnFormClosingSaveBounds;
    }

    /// <summary>ウィンドウの最小サイズを DPI に合わせて取得します。</summary>
    /// <value>スケール済みの最小サイズ。</value>
    private Size ScaledMinimumSize
    {
        get
        {
            var scale = DeviceDpi / 96.0;
            return new Size(
                (int)Math.Round(MinimumWidth * scale),
                (int)Math.Round(MinimumHeight * scale));
        }
    }

    /// <summary>画面を構築します。</summary>
    /// <remarks>枠を持たないウィンドウでは、Windows は移動とリサイズの操作を提供しません。<br/>
    /// リサイズは外周の領域をフォーム自身が所有することで実現するため、
    /// <see cref="Control.Padding"/> を枠の太さ分だけ確保し、その領域で <c>WM_NCHITTEST</c> に応答します。<br/>
    /// 移動はタイトルバーのマウス押下から OS のドラッグ操作を開始します。</remarks>
    private void BuildLayout()
    {
        MinimumSize = ScaledMinimumSize;

        // 外周をフォーム自身が所有する（子コントロールが覆うとリサイズのヒットテストが届かない）
        Padding = new Padding(AppTheme.ResizeBorderWidth);
        BackColor = SystemColors.Window;

        BuildTitleBar();
        BuildNavigationPane();

        _contentHost.Dock = DockStyle.Fill;
        _contentHost.BackColor = SystemColors.Window;

        var body = new Panel { Dock = DockStyle.Fill };
        body.Controls.Add(_contentHost);
        body.Controls.Add(_navFooter);
        body.Controls.Add(_navList);

        Controls.Add(body);
        Controls.Add(_titleBar);
    }

    /// <summary>タイトルバーを構築します。</summary>
    private void BuildTitleBar()
    {
        _titleBar.Dock = DockStyle.Top;
        _titleBar.Height = AppTheme.TitleBarHeight;
        _titleBar.BackColor = AppTheme.TitleBarBackground;

        _appNameLabel.Text = WindowTitle;
        _appNameLabel.Font = AppTheme.CreateSectionHeaderFont();
        _appNameLabel.AutoSize = true;

        _clipboardToggle.Checked = AppSetting.IsClipboardConvertEnabled;
        _clipboardToggle.AccessibleName = ClipboardToggleLabelText;
        _clipboardToggle.Anchor = AnchorStyles.Left;

        _clipboardToggleLabel.Text = ClipboardToggleLabelText;
        _clipboardToggleLabel.AutoSize = true;

        ConfigureCaptionButton(_minimizeButton, "—");
        _minimizeButton.Click += (_, _) => WindowState = FormWindowState.Minimized;

        ConfigureCaptionButton(_maximizeButton, MaximizeGlyph);
        _maximizeButton.Click += (_, _) => WindowState = WindowState == FormWindowState.Maximized
            ? FormWindowState.Normal
            : FormWindowState.Maximized;

        ConfigureCaptionButton(_closeButton, "✕");
        _closeButton.Click += (_, _) => Close();

        ConfigureCaptionButton(_navToggleButton, NavToggleGlyph);
        _navToggleButton.Click += (_, _) => ToggleNavigationPane();

        _titleBar.Controls.Add(_navToggleButton);
        _titleBar.Controls.Add(_appNameLabel);
        _titleBar.Controls.Add(_clipboardToggle);
        _titleBar.Controls.Add(_clipboardToggleLabel);
        _titleBar.Controls.Add(_presetCombo);
        _titleBar.Controls.Add(_minimizeButton);
        _titleBar.Controls.Add(_maximizeButton);
        _titleBar.Controls.Add(_closeButton);
        _titleBar.Resize += (_, _) => LayoutTitleBar();

        // プリセットの増減でドロップダウンに必要な幅が変わるため、一覧の変更時に配置をやり直す
        ViewModel.Settings.PresetNames.CollectionChanged += (_, _) => LayoutTitleBar();

        // タイトルバーの余白と、操作を受け付けないラベルの上ではウィンドウをドラッグ移動できるようにする
        // （操作系コントロールはそれぞれのマウスイベントを処理するため、ここには届かない）
        foreach (var surface in new Control[] { _titleBar, _appNameLabel, _clipboardToggleLabel })
        {
            surface.MouseDown += OnTitleBarMouseDown;
            surface.MouseDoubleClick += OnTitleBarMouseDoubleClick;
        }
    }

    /// <summary>タイトルバーの余白でマウスが押されたときに、ウィンドウのドラッグ移動を開始します。</summary>
    /// <param name="sender">イベントソース。</param>
    /// <param name="e">マウスイベントデータ。</param>
    /// <remarks>移動するのは左ボタンのみとし、最大化中は通常状態へ戻す操作を OS に委ねます。</remarks>
    private void OnTitleBarMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        WindowChrome.BeginDrag(Handle, WindowChrome.HtCaption);
    }

    /// <summary>タイトルバーの余白がダブルクリックされたときに、最大化と復元を切り替えます。</summary>
    /// <param name="sender">イベントソース。</param>
    /// <param name="e">マウスイベントデータ。</param>
    private void OnTitleBarMouseDoubleClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        WindowState = WindowState == FormWindowState.Maximized
            ? FormWindowState.Normal
            : FormWindowState.Maximized;
    }

    /// <summary>ウィンドウ操作ボタンの外観と基本設定を行います。</summary>
    /// <param name="button">設定するボタン。</param>
    /// <param name="glyph">表示する文字。</param>
    private static void ConfigureCaptionButton(Button button, string glyph)
    {
        button.Text = glyph;
        button.AutoSize = false;
        button.Width = AppTheme.CaptionButtonWidth;
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.BackColor = AppTheme.TitleBarBackground;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = AppTheme.CaptionHoverBackground;
    }

    /// <summary>ナビゲーションペインの幅を切り替えます。</summary>
    private void ToggleNavigationPane()
    {
        _isNavPaneExpanded = !_isNavPaneExpanded;
        ApplyNavigationPaneWidth();
    }

    /// <summary>ナビゲーションペインの幅を現在の開閉状態へ反映します。</summary>
    /// <remarks>折りたたみ時はアイコンのみを表示できる幅へ狭めます。</remarks>
    private void ApplyNavigationPaneWidth()
    {
        var scale = DeviceDpi / 96.0;
        var logicalWidth = _isNavPaneExpanded ? AppTheme.NavPaneWidth : AppTheme.NavPaneCompactWidth;

        _navList.Width = (int)Math.Round(logicalWidth * scale);
        _navList.Invalidate();

        // 折りたたみ時は 2 行の版表示が入らないため隠す（アイコンだけの幅に収める）
        _navFooter.Visible = _isNavPaneExpanded;
    }

    /// <summary>ナビゲーションペインを構築します。</summary>
    private void BuildNavigationPane()
    {
        // 下端の版表示は Dock=Bottom で先に追加する（後から足すと Left の一覧が下まで伸びて隠れる）
        _navFooter.Dock = DockStyle.Bottom;
        _navFooter.Height = (int)Math.Round(AppTheme.NavFooterHeight * (DeviceDpi / 96.0));
        _navFooter.TextAlign = ContentAlignment.MiddleCenter;
        _navFooter.Font = new Font(_navFooter.Font.FontFamily, 8.25f);
        _navFooter.ForeColor = AppTheme.NavFooterForeground;
        _navFooter.BackColor = AppTheme.NavPaneBackground;
        _navFooter.Text = AppVersion.For("WinForms");

        _navList.Dock = DockStyle.Left;
        _navList.BorderStyle = BorderStyle.None;
        _navList.BackColor = AppTheme.NavPaneBackground;
        _navList.DrawMode = DrawMode.OwnerDrawFixed;
        _navList.ItemHeight = AppTheme.NavItemHeight;
        _navList.IntegralHeight = false;
        _navList.DrawItem += OnNavDrawItem;
        _navList.SelectedIndexChanged += OnNavSelectedIndexChanged;
        _navList.Items.AddRange([.. ViewModel.NavItems]);
        ApplyNavigationPaneWidth();
    }

    /// <summary>ViewModel の変更を購読します。</summary>
    private void BindViewModel()
    {
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        AppSetting.PropertyChanged += OnAppSettingPropertyChanged;

        _clipboardToggle.CheckedChanged += (_, _) =>
        {
            if (AppSetting.IsClipboardConvertEnabled == _clipboardToggle.Checked) return;
            AppSetting.IsClipboardConvertEnabled = _clipboardToggle.Checked;
        };

        _navList.SelectedIndex = 0;
    }

    /// <summary>タイトルバー内のコントロールを左から順に配置します。</summary>
    /// <remarks>装飾を持たないウィンドウのため、位置は表示領域の幅から算出します。</remarks>
    private void LayoutTitleBar()
    {
        var scale = DeviceDpi / 96.0;
        var barHeight = _titleBar.ClientSize.Height;
        var padding = (int)Math.Round(AppTheme.TitleBarPadding * scale);
        var spacing = (int)Math.Round(AppTheme.TitleBarSpacing * scale);
        var switchSpacing = (int)Math.Round(AppTheme.SwitchLabelSpacing * scale);
        var buttonWidth = (int)Math.Round(AppTheme.CaptionButtonWidth * scale);
        var navToggleWidth = (int)Math.Round(AppTheme.NavToggleButtonWidth * scale);

        _minimizeButton.SetBounds(_titleBar.ClientSize.Width - (3 * buttonWidth), 0, buttonWidth, barHeight);
        _maximizeButton.SetBounds(_titleBar.ClientSize.Width - (2 * buttonWidth), 0, buttonWidth, barHeight);
        _closeButton.SetBounds(_titleBar.ClientSize.Width - buttonWidth, 0, buttonWidth, barHeight);
        _navToggleButton.SetBounds(0, 0, navToggleWidth, barHeight);

        var x = navToggleWidth + spacing;
        PlaceVertically(_appNameLabel, x, barHeight);
        x += _appNameLabel.Width + spacing;

        PlaceVertically(_clipboardToggle, x, barHeight);
        x += _clipboardToggle.Width + switchSpacing;

        PlaceVertically(_clipboardToggleLabel, x, barHeight);
        x += _clipboardToggleLabel.Width + spacing;

        var comboHeight = _presetCombo.PreferredHeight;
        var comboWidth = Math.Min(
            ResolvePresetComboWidth(scale),
            Math.Max(0, _titleBar.ClientSize.Width - (buttonWidth * 3) - x - padding));

        _presetCombo.SetBounds(x, (barHeight - comboHeight) / 2, comboWidth, comboHeight);

        // 配置を変えたことで以前の位置に残った描画（子コントロールが別の位置へ描いた内容）を消す
        _titleBar.Invalidate(true);
    }

    /// <summary>プリセット選択ドロップダウンに必要な幅を、項目の文字幅から求めます。</summary>
    /// <param name="scale">DPI 倍率。</param>
    /// <returns>物理ピクセルで表したドロップダウンの幅。</returns>
    /// <remarks>項目の文字がすべて表示される幅とし、上下限で補正します。</remarks>
    private int ResolvePresetComboWidth(double scale)
    {
        var widest = 0;
        foreach (var item in _presetCombo.Items)
        {
            if (item is not string name) continue;
            widest = Math.Max(widest, TextRenderer.MeasureText(name, _presetCombo.Font).Width);
        }

        var chrome = (int)Math.Round(PresetComboChromeWidth * scale);
        var min = (int)Math.Round(PresetComboMinWidth * scale);
        var max = (int)Math.Round(PresetComboMaxWidth * scale);
        return Math.Clamp(widest + chrome, min, max);
    }

    /// <summary>コントロールの縦位置を中央に揃えます。</summary>
    /// <param name="control">配置するコントロール。</param>
    /// <param name="x">左端の X 座標。</param>
    /// <param name="barHeight">タイトルバーの高さ。</param>
    private static void PlaceVertically(Control control, int x, int barHeight)
        => control.Location = new Point(x, (barHeight - control.Height) / 2);

    /// <summary>ナビゲーション項目を描画します。</summary>
    /// <param name="sender">イベントソース。</param>
    /// <param name="e">描画に必要な情報を含むイベントデータ。</param>
    /// <remarks>折りたたみ時はアイコンのみを項目の中央へ描画します。</remarks>
    private void OnNavDrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _navList.Items.Count) return;

        var item = (NavigationItem)_navList.Items[e.Index]!;
        var isSelected = (e.State & DrawItemState.Selected) != 0;
        var background = isSelected ? AppTheme.NavSelectedBackground : AppTheme.NavPaneBackground;

        using (var brush = new SolidBrush(background))
            e.Graphics.FillRectangle(brush, e.Bounds);

        var scale = DeviceDpi / 96.0;
        var padding = (int)Math.Round(AppTheme.NavItemPadding * scale);
        var iconSpacing = (int)Math.Round(AppTheme.NavIconSpacing * scale);
        var iconSize = (int)Math.Round(NavIconSize * scale);
        var iconTop = e.Bounds.Top + ((e.Bounds.Height - iconSize) / 2);

        if (!_isNavPaneExpanded)
        {
            var centeredLeft = e.Bounds.Left + ((e.Bounds.Width - iconSize) / 2);
            e.Graphics.DrawImage(FluentIcons.Get(item.Icon), centeredLeft, iconTop, iconSize, iconSize);
            return;
        }

        e.Graphics.DrawImage(FluentIcons.Get(item.Icon), e.Bounds.Left + padding, iconTop, iconSize, iconSize);

        var textLeft = e.Bounds.Left + padding + iconSize + iconSpacing;
        var textBounds = new Rectangle(textLeft, e.Bounds.Top, Math.Max(0, e.Bounds.Right - textLeft), e.Bounds.Height);
        TextRenderer.DrawText(
            e.Graphics, item.Label, Font, textBounds, SystemColors.ControlText,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    /// <summary>ナビゲーションの選択変更を ViewModel へ反映します。</summary>
    /// <param name="sender">イベントソース。</param>
    /// <param name="e">イベントデータ。</param>
    private void OnNavSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_isUpdatingUi) return;
        ViewModel.SelectedNavItem = _navList.SelectedItem as NavigationItem;
    }

    /// <summary>ViewModel のプロパティ変更を画面へ反映します。</summary>
    /// <param name="sender">イベントソース。</param>
    /// <param name="e">変更されたプロパティ名を含むイベントデータ。</param>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainWindowViewModel.CurrentView):
                ShowCurrentView();
                break;

            case nameof(MainWindowViewModel.SelectedNavItem):
                SyncNavigationSelection();
                break;

            default:
                break;
        }
    }

    /// <summary>AppSetting の変更をタイトルバーへ反映します。</summary>
    /// <param name="sender">イベントソース。</param>
    /// <param name="e">変更されたプロパティ名を含むイベントデータ。</param>
    private void OnAppSettingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(AppSetting.IsClipboardConvertEnabled), StringComparison.Ordinal)) return;

        _isUpdatingUi = true;
        try
        {
            _clipboardToggle.Checked = AppSetting.IsClipboardConvertEnabled;
        }
        finally
        {
            _isUpdatingUi = false;
        }
    }

    /// <summary>ViewModel の選択状態をナビゲーションの選択へ反映します。</summary>
    private void SyncNavigationSelection()
    {
        var selected = ViewModel.SelectedNavItem;
        if (selected is null) return;

        var index = ViewModel.NavItems.ToList().IndexOf(selected);
        if (index < 0 || _navList.SelectedIndex == index) return;

        _isUpdatingUi = true;
        try
        {
            _navList.SelectedIndex = index;
        }
        finally
        {
            _isUpdatingUi = false;
        }
    }

    /// <summary>現在の画面をコンテンツ領域へ表示します。</summary>
    private void ShowCurrentView()
    {
        var view = ViewModel.CurrentView;
        if (view is null) return;
        if (_contentHost.Controls.Count == 1 && ReferenceEquals(_contentHost.Controls[0], view)) return;

        _contentHost.SuspendLayout();
        try
        {
            // ビューはキャッシュして再利用するため破棄しない（Controls.Clear は破棄しない）
            _contentHost.Controls.Clear();
            view.Dock = DockStyle.Fill;
            _contentHost.Controls.Add(view);
        }
        finally
        {
            _contentHost.ResumeLayout(true);
        }
    }

    /// <summary>表示直前に保存されたウィンドウ位置とサイズを復元します。</summary>
    /// <param name="e">イベントデータ。</param>
    /// <remarks>処理フロー: <br/>
    /// 1. 保存されたサイズを DPI 換算して適用<br/>
    /// 2. 保存位置が無い場合は中央表示のまま終了<br/>
    /// 3. 保存位置を表示領域内へ補正して適用<br/><br/>
    /// 注意点: <br/>
    /// - 自動スケーリングの完了後に適用するため <see cref="Form.OnLoad"/> で行います（初回表示前のためちらつきません）
    /// </remarks>
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_isBoundsRestored) return;
        _isBoundsRestored = true;

        var scale = DeviceDpi / 96.0;
        ClientSize = new Size(
            ScreenVisibleArea.ToPhysical(AppSetting.WindowWidth, scale),
            ScreenVisibleArea.ToPhysical(AppSetting.WindowHeight, scale));

        if (AppSetting.WindowX is not double savedX || AppSetting.WindowY is not double savedY)
        {
            _lastNormalClientSize = ClientSize;
            _lastNormalPosition = Location;
            return;
        }

        var clamped = ScreenVisibleArea.Clamp(
            new EsPoint(savedX, savedY),
            new EsSize(AppSetting.WindowWidth, AppSetting.WindowHeight),
            scale);

        Location = new Point(
            ScreenVisibleArea.ToPhysical(clamped.X, scale),
            ScreenVisibleArea.ToPhysical(clamped.Y, scale));
        _lastNormalClientSize = ClientSize;
        _lastNormalPosition = Location;
    }

    /// <summary>ウィンドウのサイズ変更時に、通常状態のサイズを記録します。</summary>
    /// <param name="sender">イベントソース。</param>
    /// <param name="e">イベントデータ。</param>
    private void OnResizeTrackNormalBounds(object? sender, EventArgs e)
    {
        if (WindowState == FormWindowState.Maximized)
        {
            // 枠が無いウィンドウは既定でタスクバーを覆うため、作業領域へ収める
            MaximizedBounds = Screen.FromControl(this).WorkingArea;
            _maximizeButton.Text = RestoreGlyph;
        }
        else
        {
            _maximizeButton.Text = MaximizeGlyph;
        }

        _maximizeButton.Invalidate();
        LayoutTitleBar();
        TrackNormalBounds();

        // 外周のリサイズ領域はサイズに追従して描き直す
        Invalidate();
    }

    /// <summary>ウィンドウの移動時に、通常状態の位置を記録します。</summary>
    /// <param name="sender">イベントソース。</param>
    /// <param name="e">イベントデータ。</param>
    private void OnMoveTrackNormalBounds(object? sender, EventArgs e) => TrackNormalBounds();

    /// <summary>通常状態での位置とサイズを記録します。</summary>
    /// <remarks>最大化/最小化中の値は通常状態へ戻ったときの値ではないため記録しません。</remarks>
    private void TrackNormalBounds()
    {
        if (WindowState != FormWindowState.Normal) return;
        _lastNormalClientSize = ClientSize;
        _lastNormalPosition = Location;
    }

    /// <summary>ウィンドウクローズ時に現在のサイズと位置を保存します。</summary>
    /// <param name="sender">イベントソース。</param>
    /// <param name="e">イベントデータ。</param>
    /// <remarks>サイズと位置は DIP へ換算して保存します。<br/>
    /// デバウンス（300ms）待ちではプロセス終了に間に合わないため、同期で保存します。</remarks>
    private void OnFormClosingSaveBounds(object? sender, FormClosingEventArgs e)
    {
        var scale = DeviceDpi / 96.0;
        var isNormal = WindowState == FormWindowState.Normal;
        var size = isNormal ? ClientSize : _lastNormalClientSize;
        var position = _lastNormalPosition;

        AppSetting.WindowWidth = ScreenVisibleArea.ToDip(size.Width, scale);
        AppSetting.WindowHeight = ScreenVisibleArea.ToDip(size.Height, scale);
        AppSetting.WindowX = ScreenVisibleArea.ToDip(position.X, scale);
        AppSetting.WindowY = ScreenVisibleArea.ToDip(position.Y, scale);

        AppSetting.SaveNow();
        ViewModel.Settings.ConvertConfig.SaveNow();
    }

    /// <summary>枠を持たないウィンドウの外周（リサイズ領域）を描画します。</summary>
    /// <param name="e">描画イベントデータ。</param>
    /// <remarks>枠が無いため外周はフォーム自身が描画します。<br/>
    /// 子コントロールが上書き描画するため、外周の領域より広めに塗っても表示には影響しません。<br/>
    /// 左辺はナビゲーションペイン、上辺はタイトルバーの背景に合わせ、ウィンドウの範囲を 1px の境界線で示します。</remarks>
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var graphics = e.Graphics;
        var titleBottom = _titleBar.Bottom;

        using (var titleBrush = new SolidBrush(AppTheme.TitleBarBackground))
            graphics.FillRectangle(titleBrush, 0, 0, ClientSize.Width, titleBottom);

        if (ClientSize.Height > titleBottom && Padding.Left > 0)
        {
            using var navBrush = new SolidBrush(AppTheme.NavPaneBackground);
            graphics.FillRectangle(navBrush, 0, titleBottom, Padding.Left, ClientSize.Height - titleBottom);
        }

        using var borderPen = new Pen(AppTheme.BorderColor);
        graphics.DrawRectangle(borderPen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }

    /// <summary>マウス位置に応じたヒットテスト結果を返し、リサイズを有効にします。</summary>
    /// <param name="m">処理中のメッセージ。</param>
    /// <remarks>フォームが所有する外周領域（<see cref="Control.Padding"/> の領域）でのみ応答します。<br/>
    /// 子コントロールが覆う領域には <c>WM_NCHITTEST</c> が届かないため、その内側は対象外です。</remarks>
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        if (m.Msg != WmNcHitTest || (int)m.Result != WindowChrome.HtClient)
            return;

        if (WindowState != FormWindowState.Normal)
            return;

        var pointer = (long)m.LParam;
        var screenPoint = new Point(
            unchecked((short)(pointer & 0xFFFF)),
            unchecked((short)((pointer >> 16) & 0xFFFF)));

        var hit = HitTestBorder(PointToClient(screenPoint));
        if (hit != 0)
            m.Result = hit;
    }

    /// <summary>ウィンドウの枠・角に当たっているかを判定します。</summary>
    /// <param name="point">クライアント座標の判定位置。</param>
    /// <returns>ヒットテスト結果。枠に当たっていない場合は 0。</returns>
    /// <remarks>枠の太さは <see cref="Control.Padding"/> と一致させます（自動スケーリングで同じ倍率になるため）。</remarks>
    private int HitTestBorder(Point point)
    {
        var isLeft = point.X < Padding.Left;
        var isRight = point.X >= ClientSize.Width - Padding.Right;
        var isTop = point.Y < Padding.Top;
        var isBottom = point.Y >= ClientSize.Height - Padding.Bottom;

        return (isLeft, isRight, isTop, isBottom) switch
        {
            (true, _, true, _) => WindowChrome.HtTopLeft,
            (_, true, true, _) => WindowChrome.HtTopRight,
            (true, _, _, true) => WindowChrome.HtBottomLeft,
            (_, true, _, true) => WindowChrome.HtBottomRight,
            (true, _, _, _) => WindowChrome.HtLeft,
            (_, true, _, _) => WindowChrome.HtRight,
            (_, _, true, _) => WindowChrome.HtTop,
            (_, _, _, true) => WindowChrome.HtBottom,
            _ => 0,
        };
    }
}
