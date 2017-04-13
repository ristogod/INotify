namespace INotify.Core.Internal
{
    public sealed class Property
    {
        #region constructors

        internal Property(string name) => Name = name;

        #endregion

        #region properties

        public string Name { get; }

        #endregion

        #region methods

        public override bool Equals(object obj) => Name.Equals((obj as Property)?.Name ?? obj as string);
        public bool Equals(string name) => Name.Equals(name);
        public override int GetHashCode() => Name.GetHashCode();
        public override string ToString() => Name;

        #endregion
    }
}
