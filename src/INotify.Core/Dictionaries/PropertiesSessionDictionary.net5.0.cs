#if NET5_0
using System.Collections.Concurrent;
using INotify.Core.EventArguments;
using static System.String;

namespace INotify.Core.Dictionaries
{
    sealed class PropertiesSessionDictionary : ConcurrentDictionary<long, ReactToPropertyDictionary>
    {
        #region methods

        public bool TrackReaction(Notifier notifier, ReactToPropertyEventArgs args)
        {
            if (args.Session is <= 0 || IsNullOrWhiteSpace(args.PropertyName))
                return false;

            var tracked = true;

            AddOrUpdate(args.Session,
                        _ =>
                        {
                            PropertyChangesDictionary pd = new();
                            pd.TryAdd(args.PropertyName, args);

                            ReactToPropertyDictionary nd = new();
                            nd.TryAdd(notifier, pd);

                            return nd;
                        },
                        (_, nd) =>
                        {
                            nd.AddOrUpdate(notifier,
                                           _ =>
                                           {
                                               PropertyChangesDictionary pd = new();
                                               pd.TryAdd(args.PropertyName, args);

                                               return pd;
                                           },
                                           (_, pd) =>
                                           {
                                               tracked = pd.TryAdd(args.PropertyName, args);

                                               return pd;
                                           });

                            return nd;
                        });

            return tracked;
        }

        #endregion
    }
}
#endif
