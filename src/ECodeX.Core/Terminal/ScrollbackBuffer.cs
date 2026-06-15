namespace ECodeX.Core.Terminal;

/// <summary>
/// 固定容量的循环缓冲区，用于终端回滚行。
/// 提供 O(1) 的 Add 和 RemoveOldest 操作，避免
/// List&lt;T&gt;.RemoveAt(0) 的 O(n) 开销。
/// </summary>
public sealed class ScrollbackBuffer<T>
{
    private T[] _items;
    private int _head; // 最旧项的索引
    private int _count;

    public int Count => _count;
    public int Capacity => _items.Length;

    public ScrollbackBuffer(int capacity)
    {
        _items = new T[Math.Max(1, capacity)];
    }

    public T this[int index]
    {
        get
        {
            if (index < 0 || index >= _count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _items[(_head + index) % _items.Length];
        }
    }

    /// <summary>
    /// 在末尾添加项。若已达容量，覆盖最旧的项。
    /// </summary>
    public void Add(T item)
    {
        int insertIndex = (_head + _count) % _items.Length;
        _items[insertIndex] = item;

        if (_count < _items.Length)
        {
            _count++;
        }
        else
        {
            // 缓冲区已满 — 推进 head（最旧项被覆盖）
            _head = (_head + 1) % _items.Length;
        }
    }

    /// <summary>
    /// 添加给定列表中的所有项。
    /// </summary>
    public void AddRange(IEnumerable<T> items)
    {
        foreach (var item in items)
            Add(item);
    }

    public void Clear()
    {
        Array.Clear(_items, 0, _items.Length);
        _head = 0;
        _count = 0;
    }

    /// <summary>
    /// 将所有项复制到新列表（按从最旧到最新的顺序）。
    /// </summary>
    public List<T> ToList()
    {
        var result = new List<T>(_count);
        for (int i = 0; i < _count; i++)
            result.Add(_items[(_head + i) % _items.Length]);
        return result;
    }
}
