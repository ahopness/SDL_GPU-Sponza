using System.Collections.ObjectModel;

namespace SponzaTest.Graphics;

// Mutations invalidate material preparation without scanning unchanged scenes each frame.
public sealed class VersionedCollection<T> : Collection<T>
{
    // Collection<T>'s interface enumerator boxes List<T>.Enumerator. Keep render
    // loops allocation-free when iterating the concrete collection.
    public new List<T>.Enumerator GetEnumerator() => ((List<T>)Items).GetEnumerator();

    // Shared revisions deliberately invalidate other scenes too; this avoids
    // subscriptions and remains correct when instances or materials are shared.
    public static long Revision { get; private set; }

    public void AddRange(IEnumerable<T> items)
    {
        foreach (var item in items)
            Add(item);
    }

    protected override void InsertItem(int index, T item)
    {
        base.InsertItem(index, item);
        Revision++;
    }

    protected override void SetItem(int index, T item)
    {
        base.SetItem(index, item);
        Revision++;
    }

    protected override void RemoveItem(int index)
    {
        base.RemoveItem(index);
        Revision++;
    }

    protected override void ClearItems()
    {
        base.ClearItems();
        Revision++;
    }
}
