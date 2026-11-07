using EsUtil.ZenHanConverter.App.Core.Enums;
using EsUtil.ZenHanConverter.App.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;

namespace EsUtil.ZenHanConverter.App.Core.Presentation.ViewModels;

/// <summary>設定画面の変換項目を表す ViewModel クラスです。</summary>
/// <remarks>
/// 提供機能: <br/>
/// - 項目ラベルと有効状態の公開<br/>
/// - 変換モードの排他選択（表示用の選択肢一覧と選択中ラベル）<br/>
/// - 設定変更（プリセット読み込み・インポート）への表示追従<br/><br/>
/// 特徴: <br/>
/// - プロパティアクセスは ConvertConfig の型安全な ModePropDef（Get/Set デリゲート）を介するため、
///   プロパティ名文字列による実行時名前解決・リフレクションに依存しない<br/>
/// - 排他選択の表示手段は UI フレームワークで異なるため、次の 2 通りを提供する<br/>
///   1. <see cref="Options"/>（<see cref="SegmentOption"/>）を選択肢ごとのコントロールへ双方向バインドする<br/>
///   2. <see cref="SelectedLabel"/> を選択肢を 1 つで表すコントロール（セグメントコントロール等）へ双方向バインドする<br/>
/// - 双方の経路からの変更を設定へ反映し、設定側からの変更を双方へ再同期する<br/><br/>
/// 注意点: <br/>
/// - 選択の確定（設定への反映）は本クラスが単一所有し、<see cref="SegmentOption"/> は表示状態のみを持ちます
/// </remarks>
public partial class ZenHanConvertItem : ObservableObject, IDisposable
{
    /// <summary>バインド先の変換設定インスタンス。</summary>
    private readonly ConvertConfig _config;

    /// <summary>この項目のセグメント定義（ラベル・型安全なモード定義・セグメント配列）。</summary>
    private readonly SegmentDefine _def;

    /// <summary>表示に使用するセグメント定義の配列。表示ラベルと設定値の対応付けに使用します。</summary>
    private readonly SegmentItem[] _segments;

    /// <summary>選択状態の同期中フラグ。選択肢と設定の循環更新を防止します。</summary>
    private bool _isSyncing;

    /// <summary>選択肢のグループ名を生成するための連番。</summary>
    private static int s_groupSequence;

    /// <summary>項目のラベルを取得します。</summary>
    public string Label => _def.Label;

    /// <summary>コントロールの有効状態を返します。</summary>
    public bool IsEnabled => _def.ForceEnableState ?? true;

    /// <summary>強制的な有効状態設定を取得します。</summary>
    /// <value>強制的に無効化する場合は false、指定がない場合は null。</value>
    public bool? ForceEnableState => _def.ForceEnableState;

    /// <summary>表示用の選択肢一覧を取得します。</summary>
    /// <value>セグメント定義と同じ順序の選択肢。選択状態は <see cref="SelectedLabel"/> と同期します。</value>
    public IReadOnlyList<SegmentOption> Options { get; }

    /// <summary>現在の設定値に対応するセグメントの表示ラベルを取得または設定します。</summary>
    /// <value>選択中のセグメントの表示ラベル。該当なしの場合は空文字列。</value>
    public string SelectedLabel
    {
        get
        {
            var value = _def.Mode.Get(_config);
            return _segments.FirstOrDefault(x => Equals(x.Value, value))?.Content ?? "";
        }
        set
        {
            var match = _segments.FirstOrDefault(x => string.Equals(x.Content, value, StringComparison.Ordinal));
            if (match is not null && match.IsEnabled)
            {
                var current = _def.Mode.Get(_config);
                if (!Equals(current, match.Value))
                {
                    _def.Mode.Set(_config, match.Value);
                    OnPropertyChanged(nameof(SelectedLabel));
                    OnPropertyChanged(nameof(SelectedOption));
                }
            }
        }
    }

    /// <summary>選択中の選択肢を取得または設定します。</summary>
    /// <value>選択中の <see cref="SegmentOption"/>。該当なしの場合は null。</value>
    /// <remarks>
    /// 選択肢を 1 つのコントロールで表す UI（セグメントコントロール等）の <c>SelectedItem</c> へ
    /// 双方向でバインドするための入口です。 プロパティ名の実行時解決（<c>SelectedValuePath</c>）を
    /// 使わずに済むため、Native AOT でも選択状態を同期できます。
    /// </remarks>
    public SegmentOption? SelectedOption
    {
        get => Options.FirstOrDefault(x => x.IsSelected);
        set
        {
            if (value is null) { return; }
            SelectedLabel = value.Content;
        }
    }

    /// <summary>ZenHanConvertItem の新しいインスタンスを初期化します。</summary>
    /// <param name="config">変換設定。この設定の変更を監視し、選択状態を自動更新します。</param>
    /// <param name="def">セグメント定義（ラベル、型安全なモード定義、セグメント配列）。</param>
    public ZenHanConvertItem(ConvertConfig config, SegmentDefine def)
    {
        _config = config;
        _def = def;

        // 行ごとに独立した排他選択にするため、項目ごとに一意なグループ名を割り当てる
        var groupName = $"SegmentRow{Interlocked.Increment(ref s_groupSequence)}";

        _segments = def.Segments ??
        [
            new SegmentItem("なし", ZenHanMode.None),
            new SegmentItem("半角", ZenHanMode.ToHan),
            new SegmentItem("全角", ZenHanMode.ToZen),
        ];

        Options = [.. _segments.Select(s => new SegmentOption(s.Content, groupName, s.IsEnabled))];
        foreach (var option in Options)
            option.PropertyChanged += OnOptionPropertyChanged;

        _config.PropertyChanged += OnConfigPropertyChanged;
        SyncOptionsFromConfig();
    }

    /// <summary>選択肢の選択状態が変化したときに呼び出されます。設定へ反映します。</summary>
    /// <param name="sender">イベントソース（変更された選択肢）。</param>
    /// <param name="e">変更されたプロパティ名を含むイベントデータ。</param>
    private void OnOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isSyncing || !string.Equals(e.PropertyName, nameof(SegmentOption.IsSelected), StringComparison.Ordinal)) { return; }
        if (sender is not SegmentOption option || !option.IsSelected) return;

        SelectedLabel = option.Content;
    }

    /// <summary>ConvertConfig のプロパティ変更時に選択状態を再同期します。</summary>
    /// <remarks>この項目が対象とするモードの値は不変の場合も再同期します。<br/>
    /// 再同期が軽微なため、プロパティ名の照合は行いません。</remarks>
    /// <param name="sender">イベントソース（ConvertConfig インスタンス）。</param>
    /// <param name="e">変更されたプロパティ名を含むイベントデータ。</param>
    private void OnConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        SyncOptionsFromConfig();
        OnPropertyChanged(nameof(SelectedLabel));
    }

    /// <summary>設定値に合わせて選択肢の選択状態を更新します。</summary>
    /// <remarks>選択肢からの変更を設定へ反映する経路と再入しないよう、<see cref="_isSyncing"/> で保護します。</remarks>
    private void SyncOptionsFromConfig()
    {
        _isSyncing = true;
        try
        {
            var current = SelectedLabel;
            foreach (var option in Options)
                option.IsSelected = string.Equals(option.Content, current, StringComparison.Ordinal);
        }
        finally
        {
            _isSyncing = false;
        }

        OnPropertyChanged(nameof(SelectedOption));
    }

    /// <summary>リソースを解放します。イベント購読を解除します。</summary>
    public void Dispose()
    {
        foreach (var option in Options)
            option.PropertyChanged -= OnOptionPropertyChanged;

        _config.PropertyChanged -= OnConfigPropertyChanged;
        GC.SuppressFinalize(this);
    }
}
