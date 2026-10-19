// SPDX-License-Identifier: GPL-3.0-or-later
namespace Velora.Pdf.Viewer;

/// <summary>
/// Caché que expulsa lo menos usado cuando se supera un presupuesto de memoria (en bytes).
/// Pensada para guardar las baldosas ya dibujadas. No es segura entre hilos.
/// </summary>
public sealed class ByteLruCache<TKey, TValue> where TKey : notnull
{
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _map = new();
    private readonly LinkedList<Entry> _order = new();
    private readonly Func<TValue, long> _sizeOf;
    private readonly Action<TValue>? _onEvict;

    public ByteLruCache(long budgetBytes, Func<TValue, long> sizeOf, Action<TValue>? onEvict = null)
    {
        Budget = budgetBytes;
        _sizeOf = sizeOf;
        _onEvict = onEvict;
    }

    private readonly record struct Entry(TKey Key, TValue Value, long Size);

    public long Budget { get; set; }
    public long Bytes { get; private set; }
    public int Count => _map.Count;

    public bool TryGet(TKey key, out TValue value)
    {
        if (_map.TryGetValue(key, out var node))
        {
            _order.Remove(node);
            _order.AddFirst(node);
            value = node.Value.Value;
            return true;
        }

        value = default!;
        return false;
    }

    public bool Contains(TKey key) => _map.ContainsKey(key);

    /// <summary>Entradas guardadas (sin cambiar su orden de uso).</summary>
    public IEnumerable<KeyValuePair<TKey, TValue>> Entries() => _map.Select(kv => new KeyValuePair<TKey, TValue>(kv.Key, kv.Value.Value.Value)).ToList();

    public void Set(TKey key, TValue value)
    {
        Remove(key);
        long size = _sizeOf(value);
        var node = _order.AddFirst(new Entry(key, value, size));
        _map[key] = node;
        Bytes += size;
        Trim();
    }

    public bool Remove(TKey key)
    {
        if (!_map.Remove(key, out var node)) return false;
        _order.Remove(node);
        Bytes -= node.Value.Size;
        _onEvict?.Invoke(node.Value.Value);
        return true;
    }

    public void RemoveWhere(Func<TKey, bool> predicate)
    {
        foreach (var key in _map.Keys.Where(predicate).ToList()) Remove(key);
    }

    public void Clear()
    {
        foreach (var e in _order) _onEvict?.Invoke(e.Value);
        _order.Clear();
        _map.Clear();
        Bytes = 0;
    }

    /// <summary>Expulsa lo más antiguo hasta cumplir el presupuesto (siempre se conserva la última entrada usada).</summary>
    public void Trim()
    {
        while (Bytes > Budget && _order.Count > 1)
        {
            var last = _order.Last!;
            Remove(last.Value.Key);
        }
    }
}
