using EsUtil.ZenHanConverter.App.WinUI3.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace EsUtil.ZenHanConverter.App.WinUI3.Services;

/// <summary>アプリケーション内のページ遷移を管理します。</summary>
/// <remarks>ページの事前生成とキャッシュにより、遷移時の生成コストを削減します。<br/>
/// INavigationService インターフェースを実装し、DIP による依存性注入に対応します。<br/>
/// ページ名と型の対応は静的初期化した <c>FrozenDictionary</c> が持ち、ページ インスタンスは <c>_pageCache</c> が保持する。</remarks>
public class NavigationService(IServiceProvider services) : INavigationService
{
    /// <summary>ページ名と型のマッピング。FrozenDictionary による高速・不変なルックアップ。</summary>
    private static readonly FrozenDictionary<string, Type> s_pageMap = new Dictionary<string, Type>(StringComparer.Ordinal)
    {
        ["Home"] = typeof(HomePage),
        ["Settings"] = typeof(SettingsPage),
    }.ToFrozenDictionary();

    /// <summary>キャッシュされたページインスタンス。初回アクセス時に生成され再利用されます。</summary>
    private readonly Dictionary<Type, UIElement> _pageCache = [];
    /// <summary>レイアウト解決済み(Opacity 非表示)のページタイプセット。遷移時に Visibility 変更不要。</summary>
    private readonly HashSet<Type> _preloadedPages = [];
    /// <summary>現在表示中のページタイプ。遷移時の差分判定に使用します。</summary>
    private Type? _currentPageType;

    /// <summary>ナビゲーションサービスを初期化します。</summary>
    /// <remarks>現在は初期化処理を必要としません。ページ生成は必要なタイミングに遅延して実行されます。</remarks>
    public virtual void Initialize()
    {
    }

    /// <summary>SettingsPage をバックグラウンドで完全に事前生成します。</summary>
    /// <remarks>DispatcherQueue の Low 優先度を使用して UI スレッドがアイドル状態になったタイミングで<br/>
    /// SettingsPage の XAML 解析・コントロール生成・レイアウト解決(x:Bind, DataTemplate 含む)を<br/>
    /// すべて同期的に完了させ、ContentFrame に追加します。<br/>
    /// ページは Opacity=0 &amp; IsHitTestVisible=False で非表示にするためレイアウト状態が維持され、<br/>
    /// 初回遷移時に Visibility 変更に伴う再レイアウトが発生しません。<br/>
    /// 2回目以降と同等の切替速度を実現します。<br/></remarks>
    public virtual void PreloadSettings()
    {
        var settingsType = typeof(SettingsPage);
        if (_pageCache.ContainsKey(settingsType))
            return;

        var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        if (dispatcherQueue is null)
            return;

        dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (_pageCache.ContainsKey(settingsType))
                return;

            var mainWindow = GetMainWindow();
            var grid = mainWindow.ContentFrame;
            var page = (UIElement)services.GetRequiredService(settingsType);

            // ページをグリッドに追加し、強制レイアウトで x:Bind や DataTemplate を完全解決
            grid.Children.Add(page);
            page.Visibility = Visibility.Visible;
            page.UpdateLayout();

            // Opacity=0 で非表示: レイアウト状態を維持したまま不可視にする
            page.Opacity = 0;
            page.IsHitTestVisible = false;

            _pageCache[settingsType] = page;
            _preloadedPages.Add(settingsType);
        });
    }

    /// <summary>指定されたページへ遷移します。</summary>
    /// <param name="page">遷移先を示すオブジェクト。<br/>
    /// NavigationViewItem(Tag プロパティ使用)、文字列("Home" / "Settings")、null のいずれか。</param>
    /// <remarks>遷移先が現在のページと同じ場合は処理をスキップします。<br/>
    /// null が渡された場合は何も行いません。</remarks>
    public virtual void NavigateTo(object? page)
    {
        var tag = page switch
        {
            NavigationViewItem { Tag: string t } => t,
            NavigationViewItem item when ReferenceEquals(item, GetMainWindow().NavigationView.SettingsItem) => "Settings",
            string s => s,
            _ => null,
        };
        if (tag is null) return;

        NavigateToPage(tag);
    }

    /// <summary>DIコンテナからメインウィンドウインスタンスを取得します。</summary>
    /// <returns>メインウィンドウインスタンス</returns>
    /// <exception cref="InvalidOperationException">MainWindow が DI コンテナに登録されていない場合。</exception>
    private MainWindow GetMainWindow() => services.GetRequiredService<MainWindow>();

    /// <summary>指定されたタグ名のページへ遷移します。キャッシュがあれば再利用し、なければ生成してキャッシュに追加します。</summary>
    /// <param name="tag">遷移先ページのタグ名("Home" または "Settings")</param>
    /// <remarks>同じページへの連続遷移はスキップされます(_currentPageType による比較)。<br/>
    /// 前回表示されていたページは Visibility.Collapsed または Opacity=0 で非表示になります。<br/>
    /// プリロード済みのページ(PreloadSettings)は Opacity=0 でレイアウト状態を維持したまま非表示にし、<br/>
    /// 再表示時のレイアウト再計算を回避します。通常生成のページは Visibility.Collapsed で非表示にします。<br/>
    /// ページ遷移と同時に NavigationView の選択状態も更新し、ホーム画面の歯車ボタンなどから<br/>
    /// プログラムで遷移した場合でも選択状態が同期されるようにします。</remarks>
    private void NavigateToPage(string tag)
    {
        if (!s_pageMap.TryGetValue(tag, out var pageType)) return;
        if (_currentPageType == pageType) return;

        var mainWindow = GetMainWindow();
        var grid = mainWindow.ContentFrame;
        if (!_pageCache.TryGetValue(pageType, out var targetPage))
        {
            targetPage = (UIElement)services.GetRequiredService(pageType);
            _pageCache[pageType] = targetPage;
            grid.Children.Add(targetPage);
        }

        // 現在のページを非表示にする
        if (_currentPageType is not null && _pageCache.TryGetValue(_currentPageType, out var prev))
        {
            if (_preloadedPages.Contains(_currentPageType))
            {
                // プリロード済みページ: Opacity で非表示(レイアウト維持)
                prev.Opacity = 0;
                prev.IsHitTestVisible = false;
            }
            else
            {
                // 通常ページ: Visibility で非表示
                prev.Visibility = Visibility.Collapsed;
            }
        }

        // 遷移先ページを表示する
        if (_preloadedPages.Contains(pageType))
        {
            // プリロード済みページ: Visibility.Visible は既に設定済み、Opacity で表示
            targetPage.Opacity = 1;
            targetPage.IsHitTestVisible = true;
        }
        else
        {
            targetPage.Visibility = Visibility.Visible;
        }

        _currentPageType = pageType;

        // NavigationView の選択状態を更新(プログラムからの遷移でも選択状態を同期)
        var navView = mainWindow.NavigationView;
        navView.SelectedItem = tag switch
        {
            "Home" => navView.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => string.Equals(i.Tag as string, "Home", StringComparison.Ordinal)),
            "Settings" => navView.SettingsItem,
            _ => null,
        };
    }
}

