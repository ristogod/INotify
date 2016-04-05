using System.Collections.Concurrent;

namespace INotify.Dictionaries
{
    sealed class ReactToPropertyDictionary : ConcurrentDictionary<Notifier, PropertyChangesDictionary> {}
}
