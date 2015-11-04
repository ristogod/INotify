using System.Collections.Specialized;
using INotify.Delegates;

namespace INotify.Contracts
{
    public interface IReactToCollection : INotifyCollectionChanged
    {
        event ReactToCollectionEventHandler ReactToCollection;
        void OnCollectionChanged(NotifyCollectionChangedEventArgs args);
    }
}
