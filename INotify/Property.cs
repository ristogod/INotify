using System;
using System.Collections.Generic;

namespace INotify
{
    public sealed class Property
    {
        private readonly List<Func<bool>> _conditions = new List<Func<bool>>();

        public Property(string name)
        {
            Name = name;
        }

        public bool CanRaise => _conditions.Count == 0 || _conditions.TrueForAll(condition => condition());

        public string Name { get; }

        public override bool Equals(object obj) => Name.Equals((obj as Property)?.Name ?? obj as string);

        public bool Equals(string name) => Name.Equals(name);

        public override int GetHashCode() => Name.GetHashCode();

        public void SetCondition(Func<bool> condition)
        {
            if (condition != null)
                _conditions.Add(condition);
        }

        public override string ToString() => Name;
    }
}