namespace INotify.Internal
{
    public sealed class Property
    {
        internal Property(string name)
        {
            Name = name;
        }

        public string Name { get; }
        public override bool Equals(object obj) => Name.Equals((obj as Property)?.Name ?? obj as string);
        public bool Equals(string name) => Name.Equals(name);
        public override int GetHashCode() => Name.GetHashCode();
        public override string ToString() => Name;
    }
}
