using System.Collections.Concurrent;
using System.ComponentModel;

namespace INotify.Dictionaries
{
    internal sealed class PropertyChangesDictionary : ConcurrentDictionary<string, PropertyChangedEventArgs> {}
}
