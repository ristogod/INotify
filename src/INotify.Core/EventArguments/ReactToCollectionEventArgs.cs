using System.Collections;
using System.Collections.Specialized;
using INotify.Core.Internal;

namespace INotify.Core.EventArguments
{
    public sealed class ReactToCollectionEventArgs : NotifyCollectionChangedEventArgs
    {
        #region constructors

        public ReactToCollectionEventArgs(Session session, NotifyCollectionChangedAction action) : base(action) => Session = session;
        public ReactToCollectionEventArgs(Session session, NotifyCollectionChangedAction action, object? changedItem) : base(action, changedItem) => Session = session;
        public ReactToCollectionEventArgs(Session session, NotifyCollectionChangedAction action, object? changedItem, int index) : base(action, changedItem, index) => Session = session;
        public ReactToCollectionEventArgs(Session session, NotifyCollectionChangedAction action, IList changedItems) : base(action, changedItems) => Session = session;
        public ReactToCollectionEventArgs(Session session, NotifyCollectionChangedAction action, IList changedItems, int startingIndex) : base(action, changedItems, startingIndex) => Session = session;
        public ReactToCollectionEventArgs(Session session, NotifyCollectionChangedAction action, object? newItem, object? oldItem) : base(action, newItem, oldItem) => Session = session;
        public ReactToCollectionEventArgs(Session session, NotifyCollectionChangedAction action, object? newItem, object? oldItem, int index) : base(action, newItem, oldItem, index) => Session = session;
        public ReactToCollectionEventArgs(Session session, NotifyCollectionChangedAction action, IList newItems, IList oldItems) : base(action, newItems, oldItems) => Session = session;
        public ReactToCollectionEventArgs(Session session, NotifyCollectionChangedAction action, IList newItems, IList oldItems, int startingIndex) : base(action, newItems, oldItems, startingIndex) => Session = session;
        public ReactToCollectionEventArgs(Session session, NotifyCollectionChangedAction action, object? changedItem, int index, int oldIndex) : base(action, changedItem, index, oldIndex) => Session = session;
        public ReactToCollectionEventArgs(Session session, NotifyCollectionChangedAction action, IList changedItems, int index, int oldIndex) : base(action, changedItems, index, oldIndex) => Session = session;

        #endregion

        #region properties

        public Session Session { get; internal set; }

        #endregion
    }
}
