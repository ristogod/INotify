using System.ComponentModel;

namespace INotify.Core.EventArguments
{
    public class ReactToCollectionItemPropertyEventArgs : ReactToPropertyEventArgs
    {
        #region constructors

        public ReactToCollectionItemPropertyEventArgs(Notifier item, ReactToPropertyEventArgs args)
            : base(args.Session, args.PropertyName)
        {
            Item = item;
        }

        public ReactToCollectionItemPropertyEventArgs(INotifyPropertyChanged item, ReactToPropertyEventArgs args)
            : base(args.Session, args.PropertyName)
        {
            Item = item;
        }

        #endregion

        #region properties

        public INotifyPropertyChanged Item { get; set; }

        #endregion
    }
}
