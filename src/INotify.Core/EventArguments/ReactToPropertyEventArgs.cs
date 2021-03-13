using System.ComponentModel;
using INotify.Core.Internal;

namespace INotify.Core.EventArguments
{
    public class ReactToPropertyEventArgs : PropertyChangedEventArgs
    {
        #region constructors

        public ReactToPropertyEventArgs(Session session, string? propertyName) : base(propertyName) => Session = session;

        #endregion

        #region properties

        public Session Session { get; internal set; }

        #endregion
    }
}
