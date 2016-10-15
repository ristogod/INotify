using System.Collections.Concurrent;
using System.Collections.Specialized;
using INotify.Core.Contracts;

namespace INotify.Core.Dictionaries
{
    sealed class CollectionChangesDictionary : ConcurrentDictionary<IReactToCollection, NotifyCollectionChangedEventArgs> {}
}
