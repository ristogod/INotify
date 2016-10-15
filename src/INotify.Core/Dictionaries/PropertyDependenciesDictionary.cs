using System.Collections.Concurrent;
using INotify.Core.Internal;

namespace INotify.Core.Dictionaries
{
    public sealed class PropertyDependenciesDictionary : ConcurrentDictionary<string, PropertyDependencyDefinitions>
    {
        #region methods

        internal PropertyDependencyDefinitions Get(string key) => GetOrAdd(key, new PropertyDependencyDefinitions());

        #endregion
    }
}
