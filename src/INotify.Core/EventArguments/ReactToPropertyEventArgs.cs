using System.ComponentModel;

namespace INotify.Core.EventArguments
{
    public class ReactToPropertyEventArgs : PropertyChangedEventArgs
    {
        #region constructors

        public ReactToPropertyEventArgs(long session, string? propertyName) : base(propertyName) => Session = session;

        #endregion

        #region properties

        public long Session { get; internal set; }

        #endregion
    }
}
