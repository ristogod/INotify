using System.Collections.Concurrent;
using System.ComponentModel;

namespace INotify.Core.Dictionaries
{
    sealed class PropertyChangesDictionary : ConcurrentDictionary<string, PropertyChangedEventArgs> {}
}
