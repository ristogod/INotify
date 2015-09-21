using System.Collections.Concurrent;

namespace INotify.Dictionaries
{
    internal sealed class ReactToPropertyDictionary : ConcurrentDictionary<Notifier, PropertyChangesDictionary> {}
}