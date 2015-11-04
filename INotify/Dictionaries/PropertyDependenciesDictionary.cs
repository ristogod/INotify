using System.Collections.Concurrent;
using INotify.Internal;

namespace INotify.Dictionaries
{
    public sealed class PropertyDependenciesDictionary : ConcurrentDictionary<string, PropertyDependencyDefinitions>
    {
        internal PropertyDependencyDefinitions Get(string key) => GetOrAdd(key, new PropertyDependencyDefinitions());
    }
}
