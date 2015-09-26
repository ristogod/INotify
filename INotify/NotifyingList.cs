using System.Collections.Generic;

namespace INotify
{
    public sealed class NotifyingList<T> : NotifyingCollection<T>
    {
        public NotifyingList()
        {
        }

        public NotifyingList(int capacity)
            : base(capacity)
        {
        }

        public NotifyingList(IEnumerable<T> collection)
            : base(collection)
        {
        }
    }
}
