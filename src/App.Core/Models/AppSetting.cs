using CommunityToolkit.Mvvm.ComponentModel;

namespace EsUtil.ZenHanConverter.App.Core.Models;

/// <summary>アプリケーションのウィンドウ設定を管理します。</summary>
/// <remarks>ウィンドウの位置・サイズ情報とクリップボード連携の有効/無効を JSON ファイルに自動保存・復元します。<br/>
/// 保存先: %LOCALAPPDATA%\ZenHanConverter\AppSetting.json<br/>
/// 保存は <see cref="SettingsPersistenceBase{T}"/> がデバウンス付きで行い、シリアライズは System.Text.Json のソース生成を使う。</remarks>
public partial class AppSetting : SettingsPersistenceBase<AppSetting>
{
    /// <summary>ウィンドウの幅（DIP 非依存の論理ピクセル値）。</summary>
    [ObservableProperty]
    public partial double WindowWidth { get; set; } = 1000;

    /// <summary>ウィンドウの高さ（DIP 非依存の論理ピクセル値）。</summary>
    [ObservableProperty]
    public partial double WindowHeight { get; set; } = 800;

    /// <summary>ウィンドウ左上の X 座標（画面座標・DIP）。null の場合は位置を復元しません。</summary>
    [ObservableProperty]
    public partial double? WindowX { get; set; }

    /// <summary>ウィンドウ左上の Y 座標（画面座標・DIP）。null の場合は位置を復元しません。</summary>
    [ObservableProperty]
    public partial double? WindowY { get; set; }

    /// <summary>クリップボード連携（コピーの検知・変換結果の書き戻し）の有効/無効を取得または設定します。</summary>
    /// <remarks>false の場合、アプリはクリップボードの読み書きを行いません。<br/>
    /// 全角/半角変換そのものは常に有効で、ホーム画面での手動入力に対して適用されます。</remarks>
    [ObservableProperty]
    public partial bool IsClipboardConvertEnabled { get; set; } = true;

    /// <summary>シリアライズに使用する JsonSerializerContext と型を取得します。</summary>
    protected override SerializableTypeInfo SerializeInfo
        => new(AppJsonContext.Default, typeof(AppSetting));

    /// <summary>AppSetting の新しいインスタンスを初期化します。自動保存先を %LOCALAPPDATA% 配下に設定します。</summary>
    public AppSetting()
    {
        AutoSaveFileName = AppPaths.GetFilePath("AppSetting.json");
    }

    /// <summary>読み込んだ設定を現在のインスタンスに適用します。</summary>
    /// <param name="other">読み込んだ設定インスタンス。</param>
    protected override void ApplyFrom(AppSetting other)
    {
        WindowWidth = other.WindowWidth;
        WindowHeight = other.WindowHeight;
        WindowX = other.WindowX;
        WindowY = other.WindowY;
        IsClipboardConvertEnabled = other.IsClipboardConvertEnabled;
    }
}
