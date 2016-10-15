using System.Collections.Concurrent;
using System.ComponentModel;

namespace INotify.Dictionaries
{
    sealed class PropertyChangesDictionary : ConcurrentDictionary<string, PropertyChangedEventArgs> {}
}
