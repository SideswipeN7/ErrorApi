using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace ErrorApi.Generator.Helpers;

/// <summary>
/// An <see cref="ImmutableArray{T}"/> with structural equality, so models flowing through the
/// incremental pipeline compare by content instead of by reference.
/// </summary>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    public static readonly EquatableArray<T> Empty = new(ImmutableArray<T>.Empty);

    private readonly ImmutableArray<T> _items;

    public EquatableArray(ImmutableArray<T> items) => _items = items;

    public EquatableArray(IEnumerable<T> items) => _items = items.ToImmutableArray();

    public ImmutableArray<T> AsImmutableArray() => _items.IsDefault ? ImmutableArray<T>.Empty : _items;

    public int Count => _items.IsDefault ? 0 : _items.Length;

    public T this[int index] => _items[index];

    public bool Equals(EquatableArray<T> other)
    {
        var left = AsImmutableArray();
        var right = other.AsImmutableArray();
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            foreach (var item in AsImmutableArray())
            {
                hash = (hash * 31) + (item?.GetHashCode() ?? 0);
            }

            return hash;
        }
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)AsImmutableArray()).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal static class EquatableArrayExtensions
{
    public static EquatableArray<T> ToEquatableArray<T>(this IEnumerable<T> source)
        where T : IEquatable<T> => new(source);
}
