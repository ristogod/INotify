using System.Collections.Concurrent;

namespace INotify.Dictionaries
{
    internal sealed class PropertiesSessionDictionary : ConcurrentDictionary<long, ReactToPropertyDictionary>
    {
        public void TrackReaction(Notifier notifier, ReactToPropertyEventArgs args)
        {
            if (args.Session > 0)
            {
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
                                                   pd.TryAdd(args.PropertyName, args);

                                                   return pd;
                                               });

                                return nd;
                            });
            }
        }
    }
}