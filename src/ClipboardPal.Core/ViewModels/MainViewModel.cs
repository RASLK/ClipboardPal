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
    public LocView Loc { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _searchHasError;

    /// <summary>Which of the three clip spaces the panel is showing.</summary>
    [ObservableProperty]
    private PanelSpace _space;

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

    public string EmptyMessage => Space switch
    {
        PanelSpace.Trash => _l10n["empty.trash"],
        PanelSpace.Queue => _l10n["empty.queue"],
        _ => _l10n["empty"]
    };

    public string EmptyIcon => Space switch
    {
        PanelSpace.Trash => "🗑",
        PanelSpace.Queue => "📥",
        _ => "📋"
    };

    public bool ShowClipMenuButtons => _settings.ShowClipMenuButtons;
    public bool IsQuickSelectActive => _quickSelectActive;
    public int QueueCount => _queue.Items.Count;
    public bool HasQueueItems => _queue.Items.Count > 0;

    public bool IsHistorySpace => Space == PanelSpace.History;
    public bool IsQueueSpace => Space == PanelSpace.Queue;
    public bool IsTrashSpace => Space == PanelSpace.Trash;

    /// <summary>Item count of the space on screen, shown next to the tabs.</summary>
    public int SpaceCount => Space switch
    {
        PanelSpace.Trash => TrashItems.Count,
        PanelSpace.Queue => _queue.Items.Count,
        _ => Items.Count
    };

    public string SpaceCountTooltip => Space switch
    {
        PanelSpace.Trash => _l10n["tip.trash.count"],
        PanelSpace.Queue => _l10n["tip.queue"],
        _ => _l10n["tip.count"]
    };

    public string ClearTooltip => Space switch
    {
        PanelSpace.Trash => _l10n["tip.clear.trash"],
        PanelSpace.Queue => _l10n["tip.clear.queue"],
        _ => _l10n["tip.clear.history"]
    };

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

        // A queued flag on a trashed clip is stale — the queue only holds live items.
        foreach (var t in loaded.Where(static i => i.IsInTrash && i.IsQueued))
        {
            t.IsQueued = false;
            t.QueuedAt = null;
        }
        _queue.Restore(loaded
            .Where(static i => i.IsQueued && !i.IsInTrash)
            .OrderBy(static i => i.QueuedAt ?? i.CreatedAt));

        Items.CollectionChanged += (_, _) =>
        {
            RefreshFilter();
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(SpaceCount));
        };
        TrashItems.CollectionChanged += (_, _) => OnPropertyChanged(nameof(SpaceCount));

        _queue.Changed += () =>
        {
            OnPropertyChanged(nameof(QueueCount));
            OnPropertyChanged(nameof(HasQueueItems));
            OnPropertyChanged(nameof(SpaceCount));
            // Queue membership is persisted on the items themselves.
            ScheduleSave();
            if (Space == PanelSpace.Queue)
                RefreshFilter();
        };
        // Subscribed once the collections exist: card text is refreshed item by item.
        l10n.LanguageChanged += () =>
        {
            OnPropertyChanged(nameof(EmptyMessage));
            OnPropertyChanged(nameof(SpaceCountTooltip));
            OnPropertyChanged(nameof(ClearTooltip));
            foreach (var item in Items)
                item.RefreshLocalizedText();
            foreach (var item in TrashItems)
                item.RefreshLocalizedText();
        };
        _settings.PropertyChanged += OnSettingsChanged;
        _tray.SetVisible(_settings.ShowTrayIcon);

        ApplyRetention();
        RefreshFilter();
        _ = DebounceSaveLoopAsync(_cts.Token);
        _ = RetentionLoopAsync(_cts.Token);
        _ = OcrBacklogAsync(_cts.Token);
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
            case nameof(AppSettings.ExcludedApps):
            case nameof(AppSettings.HideExcludedAppHistory):
                RefreshFilter();
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

    partial void OnSpaceChanged(PanelSpace value)
    {
        OnPropertyChanged(nameof(EmptyMessage));
        OnPropertyChanged(nameof(EmptyIcon));
        OnPropertyChanged(nameof(IsHistorySpace));
        OnPropertyChanged(nameof(IsQueueSpace));
        OnPropertyChanged(nameof(IsTrashSpace));
        OnPropertyChanged(nameof(SpaceCount));
        OnPropertyChanged(nameof(SpaceCountTooltip));
        OnPropertyChanged(nameof(ClearTooltip));
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
            // Keep the user's pattern as-is (metacharacters still work), and OR each
            // cross-layout literal so «адм» still finds «flv» when the .* toggle is on.
            // Without this, enabling regex silently disables layout-independent search.
            var pattern = SearchText;
            foreach (var variant in _searchVariants)
            {
                if (string.Equals(variant, SearchText, StringComparison.OrdinalIgnoreCase))
                    continue;
                pattern += "|" + System.Text.RegularExpressions.Regex.Escape(variant);
            }

            _searchRegex = new System.Text.RegularExpressions.Regex(
                pattern, opts, TimeSpan.FromMilliseconds(200));
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
            item.Type == ClipItemType.Image ? _l10n["type.image"] : null
        };

        if (UseRegex)
        {
            if (_searchRegex is null)
                return !SearchHasError;
            try
            {
                // Empty strings are skipped, not matched: OcrText is "" on a picture checked
                // and found to hold no text, and a pattern like ^$ must not surface all of them.
                return haystacks.Any(h => !string.IsNullOrEmpty(h) && _searchRegex.IsMatch(h));
            }
            catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
            {
                return false;
            }
        }

        var literalComparison = _settings.SearchCaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        return haystacks.Any(h =>
        {
            if (string.IsNullOrEmpty(h))
                return false;

            foreach (var variant in _searchVariants)
            {
                // Cross-layout folds are stored lowercased; always ignore case for those
                // so «адм» still hits «FLV» when case-sensitive mode is on.
                var cmp = string.Equals(variant, SearchText, StringComparison.Ordinal)
                    ? literalComparison
                    : StringComparison.OrdinalIgnoreCase;
                if (h.Contains(variant, cmp))
                    return true;
            }

            return false;
        });
    }

    public void RefreshFilter()
    {
        var source = Space switch
        {
            PanelSpace.Trash => TrashItems.AsEnumerable(),
            PanelSpace.Queue => _queue.Items.Where(static i => !i.IsInTrash),
            _ => Items.Where(static i => !i.IsInTrash)
        };
        if (_settings.HideExcludedAppHistory)
            source = source.Where(i => !_settings.IsAppExcluded(i.SourceApp));
        var matched = source.Where(Matches).Take(_settings.PopupItemLimit).ToList();
        SyncFiltered(matched);

        if (_quickSelectActive)
            AssignSlots();

        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>
    /// Reconciles FilteredItems with the target list instead of Clear+Add:
    /// unchanged cards keep their controls, so typing in search does not rebuild the whole panel.
    /// </summary>
    private void SyncFiltered(List<ClipItem> target)
    {
        var keep = new HashSet<ClipItem>(target);
        for (var i = FilteredItems.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(FilteredItems[i]))
                FilteredItems.RemoveAt(i);
        }

        for (var i = 0; i < target.Count; i++)
        {
            if (i < FilteredItems.Count && ReferenceEquals(FilteredItems[i], target[i]))
                continue;

            var current = FilteredItems.IndexOf(target[i]);
            if (current >= 0)
                FilteredItems.Move(current, i);
            else
                FilteredItems.Insert(i, target[i]);
        }
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
                            // Item is already bound to cards — mutate observable state on the UI thread only.
                            _dispatcher.Post(() =>
                            {
                                item.LinkPreviewTitle = preview.Title;
                                item.LinkPreviewDescription = preview.Description;
                                if (string.IsNullOrWhiteSpace(item.Title))
                                    item.Title = preview.Title;
                                ScheduleSave();
                                RefreshFilter();
                            });
                        }
                    }
                }
            }

            await RecognizeImageAsync(item, _cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch
        {
            // Enrichment is best-effort.
        }
    }

    /// <summary>
    /// Every captured picture is recognized right away — search and paste-as-text then work on
    /// its contents without any setting to remember.
    /// </summary>
    private async Task RecognizeImageAsync(ClipItem item, CancellationToken cancellationToken)
    {
        // OcrStatus is already set when the clip arrives from region recognition — that pass
        // has run, and repeating it would only overwrite the result with the same work.
        if (item.Type != ClipItemType.Image || item.OcrStatus != OcrStatus.None || item.ImagePath is null)
            return;

        try
        {
            _dispatcher.Post(() => item.OcrStatus = OcrStatus.Running);

            var result = await _ocr.RecognizeAsync(item.ImagePath, cancellationToken).ConfigureAwait(false);
            var recognized = result.Outcome == OcrOutcome.Success ? result.Text : null;

            _dispatcher.Post(() =>
            {
                if (!string.IsNullOrWhiteSpace(recognized))
                {
                    item.OcrText = recognized;
                    item.OcrError = null;
                    item.OcrStatus = OcrStatus.Done;
                    if (string.IsNullOrWhiteSpace(item.Title))
                    {
                        var line = recognized.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? recognized;
                        item.Title = line.Length > 48 ? line[..48] + "…" : line;
                    }
                    ScheduleSave();
                }
                else if (result.Outcome is OcrOutcome.Success or OcrOutcome.NoText)
                {
                    item.OcrStatus = OcrStatus.NoText;
                    // Empty string, not null: the difference marks "checked, nothing found" in the
                    // saved history, so the startup backlog pass does not re-read it every launch.
                    item.OcrText = string.Empty;
                    ScheduleSave();
                }
                else
                {
                    item.OcrStatus = OcrStatus.Failed;
                    item.OcrError = result.Error;
                }

                RefreshFilter();
            });
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            // A card must never stay stuck on "recognizing".
            _dispatcher.Post(() =>
            {
                if (item.OcrStatus != OcrStatus.Running)
                    return;
                item.OcrStatus = OcrStatus.Failed;
                item.OcrError = ex.Message;
            });
        }
    }

    /// <summary>
    /// Recognizes images that predate always-on OCR (their OcrText is still null) so search
    /// covers them too. One at a time: fresh captures share the same single-slot gate inside
    /// the OCR service, so the backlog never starves them for long.
    /// </summary>
    private async Task OcrBacklogAsync(CancellationToken cancellationToken)
    {
        var backlog = Items
            .Where(static i => i.Type == ClipItemType.Image &&
                               i.OcrStatus == OcrStatus.None &&
                               i.OcrText is null &&
                               i.ImagePath is not null)
            .ToList();

        foreach (var item in backlog)
        {
            if (cancellationToken.IsCancellationRequested)
                return;
            await RecognizeImageAsync(item, cancellationToken).ConfigureAwait(false);
        }
    }

    public void RequestPaste(ClipItem item)
    {
        // Activating a trashed clip from any path (click, Enter, quick slot) restores it —
        // pasting deleted content would betray the trash space's contract.
        if (item.IsInTrash)
        {
            Restore(item);
            return;
        }

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

        // In the queue space the bin only takes the clip out of the queue — the clip itself
        // lives in history and deleting it for real from here would be a nasty surprise.
        if (Space == PanelSpace.Queue)
        {
            _queue.Remove(item);
            return;
        }

        DeleteCore(item);
        ScheduleSave();
        RefreshFilter();
    }

    private void DeleteCore(ClipItem item)
    {
        _queue.Remove(item);

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

        // The startup backlog only walks live history, so a picture that predates always-on
        // OCR and spent that time in the trash would never become searchable.
        if (item.Type == ClipItemType.Image && item.OcrText is null && item.ImagePath is not null)
            _ = RecognizeImageAsync(item, _cts.Token);
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
    private void Rename(ClipItem? item) => item?.BeginEdit();

    [RelayCommand]
    private void ToggleQueue(ClipItem? item)
    {
        if (item is null || item.IsInTrash) return;
        _queue.Toggle(item);
    }

    /// <summary>Takes a pasted clip out of the queue; it stays in history.</summary>
    public void ConsumeFromQueue(ClipItem item) => _queue.Remove(item);

    [RelayCommand]
    private void SetSpace(PanelSpace space) => Space = space;

    [RelayCommand]
    private void ClearUnpinned()
    {
        if (Space == PanelSpace.Queue)
        {
            _queue.Clear();
            return;
        }

        if (Space == PanelSpace.Trash)
        {
            // In trash view the clear button empties the trash permanently.
            foreach (var item in TrashItems.ToList())
            {
                TrashItems.Remove(item);
                item.IsInTrash = false;
                HistoryStore.DeleteImage(item);
            }
            ScheduleSave();
            RefreshFilter();
            return;
        }

        ClearHistoryUnpinned();
    }

    /// <summary>
    /// Clears unpinned history regardless of which space the panel shows. The tray's
    /// "Clear history" goes through here — it must never empty the trash or the queue just
    /// because that tab happened to be open last.
    /// </summary>
    public void ClearHistoryUnpinned()
    {
        foreach (var item in Items.Where(static i => !i.IsPinned).ToList())
            DeleteCore(item);

        ScheduleSave();
        RefreshFilter();
    }

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
            // The flag guards RestoreCommand: a stale card click must not resurrect a clip
            // whose image file is already gone.
            item.IsInTrash = false;
            HistoryStore.DeleteImage(item);
        }
        _queue.Clear();
        ScheduleSave();
        // The panel can still be visible (settings opened from the tray) — drop the stale cards.
        RefreshFilter();
        await SaveNowAsync(cancellationToken).ConfigureAwait(false);
    }

    private const int TrashRetentionDays = 30;

    private void ApplyRetention()
    {
        var changed = false;

        foreach (var item in HistoryRetentionHelper.Expired(Items, _settings.HistoryRetention, DateTime.Now).ToList())
        {
            DeleteCore(item);
            changed = true;
        }

        // Trash is not covered by the history retention setting — purge it on its own schedule.
        var trashCutoff = DateTime.Now.AddDays(-TrashRetentionDays);
        foreach (var item in TrashItems.Where(i => i.TrashedAt is { } t && t < trashCutoff).ToList())
        {
            TrashItems.Remove(item);
            HistoryStore.DeleteImage(item);
            changed = true;
        }

        if (changed)
        {
            ScheduleSave();
            RefreshFilter();
        }
    }

    private async Task RetentionLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            // The tick resumes on a thread-pool thread, but ApplyRetention edits Items/TrashItems
            // and reconciles FilteredItems, which the panel is bound to — UI thread only.
            _dispatcher.Post(ApplyRetention);
        }
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
        var trimmed = false;
        while (Items.Count > _settings.MaxHistoryItems)
        {
            var victim = Items.LastOrDefault(static i => !i.IsPinned);
            if (victim is null) break;
            DeleteCore(victim);
            trimmed = true;
        }

        if (trimmed)
        {
            ScheduleSave();
            RefreshFilter();
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

    public async Task SaveNowAsync(CancellationToken cancellationToken = default)
    {
        // Items/TrashItems are UI-thread state: enumerating them from the debounce loop's pool
        // thread races card mutations and can silently lose a save. Snapshot on the UI thread.
        // When already there (shutdown path), snapshot inline — the dispatcher may stop pumping
        // before a queued callback would ever run.
        List<ClipItem> snapshot;
        if (_dispatcher.CheckAccess())
        {
            snapshot = [.. Items, .. TrashItems];
        }
        else
        {
            List<ClipItem> captured = [];
            await _dispatcher.InvokeAsync(() => captured = [.. Items, .. TrashItems]).ConfigureAwait(false);
            snapshot = captured;
        }

        await _store.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
    }

    private bool _disposed;

    public async ValueTask DisposeAsync()
    {
        // Quitting can arrive here more than once (tray item, system shutdown, updater). The
        // second pass used to throw on the disposed token source, which aborted the whole
        // teardown before the app ever asked to shut down.
        if (_disposed)
            return;
        _disposed = true;

        await _cts.CancelAsync().ConfigureAwait(false);
        // Saved before the source is dropped — the save path takes no token of its own.
        await SaveNowAsync().ConfigureAwait(false);
        _cts.Dispose();
    }
}
