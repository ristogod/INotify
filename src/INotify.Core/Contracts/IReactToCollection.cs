using System.Collections.Specialized;
using INotify.Core.Delegates;

namespace INotify.Core.Contracts
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
