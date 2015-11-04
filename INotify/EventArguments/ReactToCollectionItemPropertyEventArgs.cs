using System.ComponentModel;

namespace INotify.EventArguments
{
    public class ReactToCollectionItemPropertyEventArgs : ReactToPropertyEventArgs
    {
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

        public INotifyPropertyChanged Item { get; set; }
    }
}
