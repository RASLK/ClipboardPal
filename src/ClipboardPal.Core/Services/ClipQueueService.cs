using ClipboardPal.Core.Models;

namespace ClipboardPal.Core.Services;

/// <summary>
/// The queue is a second clipboard space next to history: clips are staged here explicitly
/// from a card and leave when pasted or removed. The panel switches between the two spaces,
/// so the queue never intercepts anything — it is just another list to look at.
/// </summary>
public sealed class ClipQueueService
{
    private readonly List<ClipItem> _items = [];

    public IReadOnlyList<ClipItem> Items => _items;

    public event Action? Changed;

    public bool Contains(ClipItem item) => _items.Contains(item);

    /// <summary>Stages the clip, or takes it back out when it is already staged.</summary>
    public void Toggle(ClipItem item)
    {
        if (Remove(item))
            return;
        _items.Add(item);
        item.IsQueued = true;
        item.QueuedAt = DateTime.Now;
        Changed?.Invoke();
    }

    public bool Remove(ClipItem item)
    {
        if (!_items.Remove(item))
            return false;
        item.IsQueued = false;
        item.QueuedAt = null;
        Changed?.Invoke();
        return true;
    }

    /// <summary>Refills the queue from clips whose queued flag survived a restart.</summary>
    public void Restore(IEnumerable<ClipItem> items)
    {
        foreach (var item in items)
        {
            if (_items.Contains(item))
                continue;
            _items.Add(item);
            item.IsQueued = true;
        }
        if (_items.Count > 0)
            Changed?.Invoke();
    }

    public void Clear()
    {
        if (_items.Count == 0)
            return;
        foreach (var item in _items)
        {
            item.IsQueued = false;
            item.QueuedAt = null;
        }
        _items.Clear();
        Changed?.Invoke();
    }
}
