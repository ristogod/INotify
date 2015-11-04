using System.Collections.Concurrent;
using System.Collections.Specialized;
using INotify.Contracts;

namespace INotify.Dictionaries
{
    internal sealed class CollectionChangesDictionary : ConcurrentDictionary<IReactToCollection, NotifyCollectionChangedEventArgs> {}
}
