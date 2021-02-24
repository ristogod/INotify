using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using INotify.Core.Extensions;

namespace INotify.Core.Internal
{
    public sealed class PropertyDependencyDefinitions
    {
        #region fields

        public readonly List<Action> Executions = new();
        internal readonly List<Property> List = new();

        #endregion

        #region constructors

        internal PropertyDependencyDefinitions() { }

        #endregion

        #region methods

        public PropertyDependencyDefinitions Execute(Action action)
        {
            Executions.Add(action);

            return this;
        }

        internal PropertyDependencyDefinitions Affects<TProp>(Expression<Func<TProp>> property) => Affects(property.GetName());

        internal PropertyDependencyDefinitions Affects(string property)
        {
            var propertyName = List.SingleOrDefault(p => p.Equals(property));

            if (propertyName is not null)
                return this;

            propertyName = new(property);
            List.Add(propertyName);

            return this;
        }

        internal void Free(string name) => List.RemoveAll(p => p.Equals(name));

        #endregion
    }
}
