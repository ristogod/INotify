using System.Collections.Concurrent;

namespace INotify.Core.Dictionaries
{
    sealed class PropertyReferenceDictionary<T> : ConcurrentDictionary<string, T>
    {
        #region methods

        public void Add(string referenceName, T reference)
        {
            if (reference is null)
                return;

            Remove(referenceName);
            TryAdd(referenceName, reference);
        }

        public T? Remove(string referenceName) =>
            TryRemove(referenceName, out var outValue)
                ? outValue
                : default;

        #endregion
    }
}
