using System.Collections.Specialized;
using INotify.Delegates;

namespace INotify.Contracts
{
    public interface IReactToCollection : INotifyCollectionChanged
    {
        #region events

        event ReactToCollectionEventHandler ReactToCollection;

        #endregion

        #region methods

        void OnCollectionChanged(NotifyCollectionChangedEventArgs args);

        #endregion
    }
}
