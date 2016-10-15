using System.Collections.Concurrent;

namespace INotify.Core.Dictionaries
{
    sealed class ReactToPropertyDictionary : ConcurrentDictionary<Notifier, PropertyChangesDictionary> {}
}
