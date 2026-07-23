using System.Collections.ObjectModel;
using ClipboardPal.Core.Abstractions;
using ClipboardPal.Core.Models;
using ClipboardPal.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipboardPal.Core.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly HistoryStore _store;
    private readonly AppSettings _settings;
    private readonly ClipQueueService _queue;
    private readonly ILinkPreviewService _linkPreview;
    private readonly IOcrService _ocr;
    private readonly ISoundService _sound;
    private readonly ITrayFeedbackService _tray;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILocalizationService _l10n;
    private readonly CancellationTokenSource _cts = new();

    private System.Text.RegularExpressions.Regex? _searchRegex;
    private IReadOnlyList<string> _searchVariants = [];
    private int _saveRequested;
    private bool _quickSelectActive;
    private int _quickModeIndex = -1;

    public ObservableCollection<ClipItem> Items { get; }
    public ObservableCollection<ClipItem> FilteredItems { get; } = [];
    public ObservableCollection<ClipItem> TrashItems { get; } = [];
    public ClipQueueService Queue => _queue;
    public LocView Loc { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _searchHasError;

    [ObservableProperty]
    private bool _showTrash;

    public bool UseRegex
    {
        get => _settings.SearchUseRegex;
        set
        {
            if (_settings.SearchUseRegex == value) return;
            _settings.SearchUseRegex = value;
            OnPropertyChanged();
            RebuildSearchPattern();
            RefreshFilter();
        }
    }

    public bool IsEmpty => FilteredItems.Count == 0;
    public string EmptyMessage => ShowTrash ? _l10n["empty.trash"] : _l10n["empty"];
    public bool ShowClipMenuButtons => _settings.ShowClipMenuButtons;
    public bool IsQuickSelectActive => _quickSelectActive;
    public int QueueCount => _queue.Items.Count;
    public bool HasQueueItems => _queue.Items.Count > 0;

    public event Action<ClipItem>? PasteRequested;
    public event Action? CapturedFeedback;

    public MainViewModel(
        HistoryStore store,
        AppSettings settings,
        ClipQueueService queue,
        ILinkPreviewService linkPreview,
        IOcrService ocr,
        ISoundService sound,
        ITrayFeedbackService tray,
        IUiDispatcher dispatcher,
        ILocalizationService l10n)
    {
        _store = store;
        _settings = settings;
        _queue = queue;
        _linkPreview = linkPreview;
        _ocr = ocr;
        _sound = sound;
        _tray = tray;
        _dispatcher = dispatcher;
        _l10n = l10n;
        Loc = new LocView(l10n);
        l10n.LanguageChanged += () => OnPropertyChanged(nameof(EmptyMessage));

        ClipItem.GlobalPreviewLength = settings.PreviewLength;

        var loaded = store.Load();
        Items = new ObservableCollection<ClipItem>(loaded
            .Where(static i => !i.IsInTrash)
            .OrderByDescending(static i => i.IsPinned)
            .ThenByDescending(static i => i.CreatedAt));

        foreach (var t in loaded.Where(static i => i.IsInTrash).OrderByDescending(static i => i.TrashedAt))
            TrashItems.Add(t);

        foreach (var item in Items)
            Attach(item);

        Items.CollectionChanged += (_, _) =>
        {
            RefreshFilter();
            OnPropertyChanged(nameof(IsEmpty));
        };

        _queue.Changed += () =>
        {
            OnPropertyChanged(nameof(QueueCount));
            OnPropertyChanged(nameof(HasQueueItems));
        };
        _settings.PropertyChanged += OnSettingsChanged;
        _tray.SetVisible(_settings.ShowTrayIcon);

        ApplyRetention();
        RefreshFilter();
        _ = DebounceSaveLoopAsync(_cts.Token);
        _ = RetentionLoopAsync(_cts.Token);
    }

    private void OnSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.PreviewLength):
                ClipItem.GlobalPreviewLength = _settings.PreviewLength;
                RefreshFilter();
                break;
            case nameof(AppSettings.MaxHistoryItems):
                TrimOverflow();
                ScheduleSave();
                break;
            case nameof(AppSettings.SearchUseRegex):
                OnPropertyChanged(nameof(UseRegex));
                RebuildSearchPattern();
                RefreshFilter();
                break;
            case nameof(AppSettings.SearchCaseSensitive):
                RebuildSearchPattern();
                RefreshFilter();
                break;
            case nameof(AppSettings.PopupItemLimit):
                RefreshFilter();
                break;
            case nameof(AppSettings.ShowClipMenuButtons):
                OnPropertyChanged(nameof(ShowClipMenuButtons));
                break;
            case nameof(AppSettings.ShowTrayIcon):
                _tray.SetVisible(_settings.ShowTrayIcon);
                break;
            case nameof(AppSettings.HistoryRetention):
                ApplyRetention();
                break;
        }
    }

    private void Attach(ClipItem item)
    {
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ClipItem.Title) or nameof(ClipItem.IsPinned))
            {
                ScheduleSave();
                RefreshFilter();
            }
        };
    }

    partial void OnSearchTextChanged(string value)
    {
        RebuildSearchPattern();
        RefreshFilter();
    }

    partial void OnShowTrashChanged(bool value)
    {
        OnPropertyChanged(nameof(EmptyMessage));
        RefreshFilter();
    }

    private void RebuildSearchPattern()
    {
        _searchRegex = null;
        SearchHasError = false;
        _searchVariants = string.IsNullOrWhiteSpace(SearchText)
            ? []
            : LayoutText.SearchVariants(SearchText);

        if (!UseRegex || string.IsNullOrWhiteSpace(SearchText))
            return;

        var opts = System.Text.RegularExpressions.RegexOptions.CultureInvariant;
        if (!_settings.SearchCaseSensitive)
            opts |= System.Text.RegularExpressions.RegexOptions.IgnoreCase;

        try
        {
            _searchRegex = new System.Text.RegularExpressions.Regex(
                SearchText, opts, TimeSpan.FromMilliseconds(200));
        }
        catch (ArgumentException)
        {
            SearchHasError = true;
        }
    }

    private bool Matches(ClipItem item)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
            return true;

        var haystacks = new[]
        {
            item.Text,
            item.OcrText,
            item.DisplayTitle,
            item.SourceApp,
            item.LinkUrl,
            item.LinkPreviewTitle,
            item.Type == ClipItemType.Image ? "изображение" : null
        };

        if (UseRegex)
        {
            if (_searchRegex is null)
                return !SearchHasError;
            try
            {
                return haystacks.Any(h => h is not null && _searchRegex.IsMatch(h));
            }
            catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
            {
                return false;
            }
        }

        var comparison = _settings.SearchCaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        return haystacks.Any(h => h is not null &&
            _searchVariants.Any(v => h.Contains(v, comparison)));
    }

    public void RefreshFilter()
    {
        var source = ShowTrash ? TrashItems.AsEnumerable() : Items.Where(static i => !i.IsInTrash);
        var matched = source.Where(Matches).Take(_settings.PopupItemLimit).ToList();
        FilteredItems.Clear();
        foreach (var item in matched)
            FilteredItems.Add(item);

        if (_quickSelectActive)
            AssignSlots();

        OnPropertyChanged(nameof(IsEmpty));
    }

    public void SetQuickSelectActive(bool active)
    {
        if (_quickSelectActive == active) return;
        _quickSelectActive = active;
        OnPropertyChanged(nameof(IsQuickSelectActive));
        if (active) AssignSlots(); else ClearSlots();
    }

    private void AssignSlots()
    {
        var n = 1;
        foreach (var item in FilteredItems)
            item.SlotNumber = n <= 10 ? n++ : 0;
    }

    private void ClearSlots()
    {
        foreach (var item in Items)
            item.SlotNumber = 0;
        foreach (var item in TrashItems)
            item.SlotNumber = 0;
    }

    public ClipItem? ItemFromSlot(int slot) =>
        FilteredItems.FirstOrDefault(i => i.SlotNumber == slot);

    public ClipItem? SelectedOrFirst() =>
        FilteredItems.FirstOrDefault(static i => i.IsSelected) ?? FilteredItems.FirstOrDefault();

    public void SelectIndex(int index)
    {
        for (var i = 0; i < FilteredItems.Count; i++)
            FilteredItems[i].IsSelected = i == index;
    }

    public void SelectLast()
    {
        if (FilteredItems.Count == 0) return;
        SelectIndex(0);
    }

    // ----- Quick mode -----

    public void QuickModeCycle()
    {
        if (FilteredItems.Count == 0) return;
        _quickModeIndex = (_quickModeIndex + 1) % FilteredItems.Count;
        SelectIndex(_quickModeIndex);
    }

    public ClipItem? QuickModeCurrent()
    {
        if (_quickModeIndex < 0 || _quickModeIndex >= FilteredItems.Count)
            _quickModeIndex = 0;
        return FilteredItems.Count == 0 ? null : FilteredItems[_quickModeIndex];
    }

    public void QuickModeReset() => _quickModeIndex = -1;

    // ----- Capture -----

    public void AddCaptured(ClipItem item)
    {
        if (!_settings.ClipboardMonitoringEnabled)
        {
            if (item.Type == ClipItemType.Image)
                HistoryStore.DeleteImage(item);
            return;
        }

        if (_settings.AutoPlainText && item.Type == ClipItemType.Text &&
            PlainTextConverter.LooksLikeHtml(item.Text))
        {
            item.Text = PlainTextConverter.ToPlain(item.Text);
            item.Hash = HistoryStore.Sha256Text(item.Text ?? string.Empty);
        }

        if (!_settings.SaveDuplicates)
        {
            var existing = Items.FirstOrDefault(i => i.Hash == item.Hash && i.Type == item.Type && !i.IsInTrash);
            if (existing is not null)
            {
                existing.CreatedAt = DateTime.Now;
                if (_settings.MoveRecentToTop)
                    MoveToTop(existing);
                if (item.Type == ClipItemType.Image)
                    HistoryStore.DeleteImage(item);
                OnCapturedFeedback();
                ScheduleSave();
                RefreshFilter();
                return;
            }
        }

        Attach(item);
        if (_settings.MoveRecentToTop)
            Items.Insert(FirstUnpinnedIndex(), item);
        else
            Items.Add(item);

        if (_settings.QueueEnabled)
            _queue.Add(item);

        TrimOverflow();
        OnCapturedFeedback();
        ScheduleSave();
        _ = EnrichAsync(item);
    }

    private void OnCapturedFeedback()
    {
        if (_settings.PlaySoundOnCopy)
            _sound.PlayCopySound(_settings.CopySound);
        if (_settings.AnimateTrayOnCopy)
            _tray.AnimateCopy();
        CapturedFeedback?.Invoke();
    }

    private async Task EnrichAsync(ClipItem item)
    {
        try
        {
            if (item.Type == ClipItemType.Text)
            {
                var url = UrlDetector.FirstUrl(item.Text);
                if (url is not null)
                {
                    item.LinkUrl = url;
                    if (_settings.FetchLinkPreview)
                    {
                        var preview = await _linkPreview.FetchAsync(url, _cts.Token).ConfigureAwait(false);
                        if (preview is not null)
                        {
                            item.LinkPreviewTitle = preview.Title;
                            item.LinkPreviewDescription = preview.Description;
                            if (string.IsNullOrWhiteSpace(item.Title))
                                item.Title = preview.Title;
                            ScheduleSave();
                            RefreshFilterUi();
                        }
                    }
                }
            }

            if (item.Type == ClipItemType.Image &&
                _settings.RecognizeTextOnImages &&
                _settings.PermissionScreenRecording &&
                item.ImagePath is not null)
            {
                var text = await _ocr.RecognizeAsync(item.ImagePath, _cts.Token).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    item.OcrText = text;
                    if (string.IsNullOrWhiteSpace(item.Title))
                    {
                        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? text;
                        item.Title = line.Length > 48 ? line[..48] + "…" : line;
                    }
                    ScheduleSave();
                    RefreshFilterUi();
                }
            }
        }
        catch
        {
            // enrichment is best-effort
        }
    }

    private void RefreshFilterUi() =>
        _dispatcher.Post(RefreshFilter);

    public void RequestPaste(ClipItem item)
    {
        MarkUsed(item);
        PasteRequested?.Invoke(item);
    }

    public void MarkUsed(ClipItem item)
    {
        item.CreatedAt = DateTime.Now;
        if (_settings.MoveRecentToTop)
            MoveToTop(item);
        ScheduleSave();
    }

    public string? TextForAction(ClipItem item, ClipAction action)
    {
        var raw = item.Text ?? item.OcrText;
        return action is ClipAction.CopyAsPlainText or ClipAction.PasteAsPlainText
            ? PlainTextConverter.ToPlain(raw)
            : raw;
    }

    [RelayCommand]
    private void Delete(ClipItem? item)
    {
        if (item is null) return;

        if (_settings.SkipTrash || item.IsInTrash)
        {
            Items.Remove(item);
            TrashItems.Remove(item);
            HistoryStore.DeleteImage(item);
        }
        else
        {
            Items.Remove(item);
            item.IsInTrash = true;
            item.TrashedAt = DateTime.Now;
            item.IsPinned = false;
            TrashItems.Insert(0, item);
        }

        ScheduleSave();
        RefreshFilter();
    }

    [RelayCommand]
    private void Restore(ClipItem? item)
    {
        if (item is null || !item.IsInTrash) return;
        TrashItems.Remove(item);
        item.IsInTrash = false;
        item.TrashedAt = null;
        Attach(item);
        Items.Insert(FirstUnpinnedIndex(), item);
        ScheduleSave();
        RefreshFilter();
    }

    [RelayCommand]
    private void TogglePin(ClipItem? item)
    {
        if (item is null || item.IsInTrash) return;
        item.IsPinned = !item.IsPinned;
        MoveToTop(item);
        ScheduleSave();
        RefreshFilter();
    }

    [RelayCommand]
    private void Rename(ClipItem? item)
    {
        if (item is not null)
            item.IsEditing = true;
    }

    [RelayCommand]
    private void AddToQueue(ClipItem? item)
    {
        if (item is null || item.IsInTrash) return;
        _queue.Add(item);
    }

    [RelayCommand]
    private void ClearQueue() => _queue.Clear();

    [RelayCommand]
    private void ClearUnpinned()
    {
        foreach (var item in Items.Where(static i => !i.IsPinned).ToList())
            Delete(item);
    }

    [RelayCommand]
    private void ToggleTrashView() => ShowTrash = !ShowTrash;

    public async Task ClearAllHistoryAsync(CancellationToken cancellationToken = default)
    {
        foreach (var item in Items.ToList())
        {
            Items.Remove(item);
            HistoryStore.DeleteImage(item);
        }
        foreach (var item in TrashItems.ToList())
        {
            TrashItems.Remove(item);
            HistoryStore.DeleteImage(item);
        }
        _queue.Clear();
        ScheduleSave();
        await SaveNowAsync(cancellationToken).ConfigureAwait(false);
    }

    private void ApplyRetention()
    {
        foreach (var item in HistoryRetentionHelper.Expired(Items, _settings.HistoryRetention, DateTime.Now).ToList())
            Delete(item);
    }

    private async Task RetentionLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            ApplyRetention();
    }

    private void MoveToTop(ClipItem item)
    {
        var oldIndex = Items.IndexOf(item);
        if (oldIndex < 0) return;
        var newIndex = item.IsPinned ? 0 : FirstUnpinnedIndex();
        if (oldIndex < newIndex) newIndex--;
        if (oldIndex != newIndex)
            Items.Move(oldIndex, newIndex);
    }

    private int FirstUnpinnedIndex()
    {
        for (var i = 0; i < Items.Count; i++)
        {
            if (!Items[i].IsPinned)
                return i;
        }
        return Items.Count;
    }

    private void TrimOverflow()
    {
        while (Items.Count > _settings.MaxHistoryItems)
        {
            var victim = Items.LastOrDefault(static i => !i.IsPinned);
            if (victim is null) break;
            Delete(victim);
        }
    }

    private void ScheduleSave() => Interlocked.Exchange(ref _saveRequested, 1);

    private async Task DebounceSaveLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            if (Interlocked.Exchange(ref _saveRequested, 0) == 1)
                await SaveNowAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public Task SaveNowAsync(CancellationToken cancellationToken = default) =>
        _store.SaveAsync(Items.Concat(TrashItems), cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        _cts.Dispose();
        await SaveNowAsync().ConfigureAwait(false);
    }
}
