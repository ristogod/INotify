using System.Collections.Concurrent;

namespace INotify.Core.Dictionaries
{
    sealed class ReferencePropertyDependenciesDictionary : ConcurrentDictionary<string, PropertyDependenciesDictionary>
    {
        #region methods

        public PropertyDependenciesDictionary Retrieve(string key) => GetOrAdd(key, new PropertyDependenciesDictionary());

        #endregion
    }
}
