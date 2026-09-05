namespace MangaViewer;

using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 固定容量のリングバッファ（上限超過時は古い要素から上書き破棄）
/// </summary>
internal class RingBuffer<T> : IEnumerable<T>
{
    private readonly T[] _buffer;
    private int _start;
    private int _count;

    public RingBuffer(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be greater than zero.");
        _buffer = new T[capacity];
    }

    public int Capacity => _buffer.Length;
    public int Count => _count;

    public void Push(T item)
    {
        int index = (_start + _count) % _buffer.Length;
        _buffer[index] = item;
        if (_count < _buffer.Length)
        {
            _count++;
        }
        else
        {
            _start = (_start + 1) % _buffer.Length;
        }
    }

    public void Clear()
    {
        Array.Clear(_buffer, 0, _buffer.Length);
        _start = 0;
        _count = 0;
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i < _count; i++)
        {
            yield return _buffer[(_start + i) % _buffer.Length];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
