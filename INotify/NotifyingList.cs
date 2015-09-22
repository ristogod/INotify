using System.Collections.Generic;

namespace INotify
{
    public sealed class NotifyingList<T> : NotifyingCollection<T>
    {
        public NotifyingList()
        {
            Initialize();
        }

        public NotifyingList(int capacity)
            : base(capacity)
        {
            Initialize();
        }

        public NotifyingList(IEnumerable<T> collection)
            : base(collection)
        {
            Initialize();
        }

        internal override void ConfigureProperties() {}
    }
}
