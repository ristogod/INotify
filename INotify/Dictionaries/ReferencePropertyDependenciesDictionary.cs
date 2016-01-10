using System.Collections.Concurrent;

namespace INotify.Dictionaries
{
    internal sealed class ReferencePropertyDependenciesDictionary : ConcurrentDictionary<string, PropertyDependenciesDictionary>
    {
        #region methods

        public PropertyDependenciesDictionary Retrieve(string key) => GetOrAdd(key, new PropertyDependenciesDictionary());

        #endregion
    }
}
