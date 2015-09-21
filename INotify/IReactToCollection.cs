using System.Collections.Specialized;

namespace INotify
{
    public interface IReactToCollection : INotifyCollectionChanged
    {
        event ReactToCollectionEventHandler ReactToCollection;

        void OnCollectionChanged(NotifyCollectionChangedEventArgs args);
    }
}