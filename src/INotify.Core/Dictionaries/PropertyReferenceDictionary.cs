using System.Collections.Concurrent;

namespace INotify.Core.Dictionaries
{
    sealed class PropertyReferenceDictionary<T> : ConcurrentDictionary<string, T>
    {
        #region methods

        public void Add(string referenceName, T reference)
        {
            if (referenceName == null || reference == null)
                return;

            Remove(referenceName);
            TryAdd(referenceName, reference);
        }

        public T Remove(string referenceName)
        {
            var outValue = default(T);
            if (referenceName != null)
                TryRemove(referenceName, out outValue);

            return outValue;
        }

        #endregion
    }
}
