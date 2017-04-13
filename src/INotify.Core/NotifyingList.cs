using System.Collections.Generic;

namespace INotify.Core
{
    public sealed class NotifyingList<T> : NotifyingCollection<T>
    {
        #region constructors

        public NotifyingList() { }

        public NotifyingList(int capacity)
            : base(capacity) { }

        public NotifyingList(IEnumerable<T> collection)
            : base(collection) { }

        #endregion
    }
}
