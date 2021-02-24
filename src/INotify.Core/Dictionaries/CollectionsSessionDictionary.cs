using System.Collections.Concurrent;
using INotify.Core.Contracts;
using INotify.Core.EventArguments;

namespace INotify.Core.Dictionaries
{
    sealed class CollectionsSessionDictionary : ConcurrentDictionary<long, CollectionChangesDictionary>
    {
        #region methods

        public void TrackReaction(IReactToCollection collection, ReactToCollectionEventArgs args)
        {
            if (args.Session is > 0)
            {
                AddOrUpdate(args.Session,
                            _ =>
                            {
                                CollectionChangesDictionary ncd = new();
                                ncd.TryAdd(collection, args);

                                return ncd;
                            },
                            (_, ncd) =>
                            {
                                ncd.TryAdd(collection, args);

                                return ncd;
                            });
            }
        }

        #endregion
    }
}
