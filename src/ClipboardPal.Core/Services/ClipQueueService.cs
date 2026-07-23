using ClipboardPal.Core.Models;

namespace ClipboardPal.Core.Services;

/// <summary>In-memory paste queue for collecting clips before bulk paste.</summary>
public sealed class ClipQueueService
{
    private readonly List<ClipItem> _items = [];
    private int _sequentialIndex;

    public IReadOnlyList<ClipItem> Items => _items;

    public event Action? Changed;

    public void Clear()
    {
        _items.Clear();
        _sequentialIndex = 0;
        Changed?.Invoke();
    }

    public void Add(ClipItem item)
    {
        _items.Add(item);
        Changed?.Invoke();
    }

    public ClipItem? TakeNext(QueuePasteMode mode)
    {
        if (_items.Count == 0) return null;

        return mode switch
        {
            QueuePasteMode.InsertNew or QueuePasteMode.First => TakeAt(0),
            QueuePasteMode.Append => TakeAt(_items.Count - 1),
            QueuePasteMode.Sequential => TakeSequential(),
            _ => TakeAt(0)
        };
    }

    private ClipItem TakeAt(int index)
    {
        var item = _items[index];
        _items.RemoveAt(index);
        _sequentialIndex = Math.Clamp(_sequentialIndex, 0, _items.Count);
        Changed?.Invoke();
        return item;
    }

    private ClipItem TakeSequential()
    {
        if (_sequentialIndex >= _items.Count)
            _sequentialIndex = 0;
        return TakeAt(_sequentialIndex);
    }
}
