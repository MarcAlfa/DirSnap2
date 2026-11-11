using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace DirSnap2.Models;

/// <summary>
/// ObservableCollection that can swap its whole content raising a single Reset
/// notification, instead of one notification per item (slow with thousands of rows).
/// </summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        CheckReentrancy();

        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
