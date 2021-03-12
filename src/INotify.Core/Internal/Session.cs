#if !NETSTANDARD2_1 && !NETCOREAPP3_1 && !NET5_0
using System;
using INotify.Core.Contracts;
using INotify.Core.Dictionaries;
using INotify.Core.EventArguments;
using static System.GC;
using static System.String;

namespace INotify.Core.Internal
{
    public class Session : IDisposable
    {
        #region fields

        internal readonly CollectionChangesDictionary CollectionChanges = new();
        internal readonly ReactToPropertyDictionary ReactToProperties = new();
        bool _disposed;

        #endregion

        #region constructors

        ~Session() => Dispose(false);

        #endregion

        #region properties

        public Guid Id { get; } = Guid.NewGuid();

        #endregion

        #region methods

        public void Dispose()
        {
            Dispose(true);
            SuppressFinalize(this);
        }

        public void TrackReaction(IReactToCollection collection, ReactToCollectionEventArgs args) => CollectionChanges.TryAdd(collection, args);

        public bool TrackReaction(Notifier notifier, ReactToPropertyEventArgs args)
        {
            if (IsNullOrWhiteSpace(args.PropertyName))
                return false;

            var tracked = true;

            ReactToProperties.AddOrUpdate(notifier,
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

            return tracked;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            EndSession();

            _disposed = true;
        }

        internal void EndSession()
        {
            foreach (var cn in CollectionChanges)
                cn.Key.OnCollectionChanged(cn.Value);

            foreach (var propertyNotification in ReactToProperties)
            {
                var notifier = propertyNotification.Key;

                if (!notifier.IsNotificationsEnabled)
                    continue;

                foreach (var pd in propertyNotification.Value)
                    notifier.OnPropertyChanged(pd.Value);
            }
        }

        #endregion
    }
}
#endif
