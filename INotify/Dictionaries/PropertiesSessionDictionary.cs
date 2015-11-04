using System.Collections.Concurrent;
using INotify.EventArguments;

namespace INotify.Dictionaries
{
    internal sealed class PropertiesSessionDictionary : ConcurrentDictionary<long, ReactToPropertyDictionary>
    {
        public bool TrackReaction(Notifier notifier, ReactToPropertyEventArgs args)
        {
            if (args.Session <= 0)
                return false;

            var tracked = true;
            AddOrUpdate(args.Session,
                        sessionKey =>
                        {
                            var pd = new PropertyChangesDictionary();
                            pd.TryAdd(args.PropertyName, args);

                            var nd = new ReactToPropertyDictionary();
                            nd.TryAdd(notifier, pd);

                            return nd;
                        },
                        (sessionKey, nd) =>
                        {
                            nd.AddOrUpdate(notifier,
                                           notifierKey =>
                                           {
                                               var pd = new PropertyChangesDictionary();
                                               pd.TryAdd(args.PropertyName, args);

                                               return pd;
                                           },
                                           (ownerKey, pd) =>
                                           {
                                               tracked = pd.TryAdd(args.PropertyName, args);

                                               return pd;
                                           });

                            return nd;
                        });

            return tracked;
        }
    }
}
