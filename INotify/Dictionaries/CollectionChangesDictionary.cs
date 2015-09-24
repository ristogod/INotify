using System.Collections.Concurrent;
using System.Collections.Specialized;

namespace INotify.Dictionaries
{
    internal sealed class CollectionChangesDictionary : ConcurrentDictionary<IReactToCollection, NotifyCollectionChangedEventArgs> {}
}
