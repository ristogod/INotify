using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using INotify.Extensions;

namespace INotify
{
    public sealed class PropertyDependencyDefinitions
    {
        internal readonly List<Action> Executions = new List<Action>();
        internal readonly List<Property> List = new List<Property>();
        internal PropertyDependencyDefinitions Affects<TProp>(Expression<Func<TProp>> property, Func<bool> condition = null) => Affects(property.GetName(), condition);

        internal PropertyDependencyDefinitions Affects(string property, Func<bool> condition = null)
        {
            var propertyName = List.SingleOrDefault(p => p.Equals(property));
            if (propertyName == null)
            {
                propertyName = new Property(property);
                List.Add(propertyName);
            }

            propertyName.SetCondition(condition);

            return this;
        }

        public PropertyDependencyDefinitions Execute(Action action)
        {
            Executions.Add(action);
            return this;
        }

        public PropertyDependencyDefinitions Execute(RelayCommand command)
        {
            Executions.Add(command.Execute);
            return this;
        }

        public PropertyDependencyDefinitions Raise(RelayCommand command)
        {
            Executions.Add(command.RaiseCanExecuteChanged);
            return this;
        }

        public PropertyDependencyDefinitions Raise<TParam>(RelayCommand<TParam> command)
        {
            Executions.Add(command.RaiseCanExecuteChanged);
            return this;
        }

        internal void Free(string name) => List.RemoveAll(p => p.Equals(name));
    }
}
