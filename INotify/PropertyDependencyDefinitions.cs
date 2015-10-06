using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Windows.Input;
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
            if (action != null)
                Executions.Add(action);

            return this;
        }

        public PropertyDependencyDefinitions Execute(ICommand command)
        {
            if (command != null)
                Executions.Add(() => command.Execute(null));

            return this;
        }

        public PropertyDependencyDefinitions IfCanExecute(ICommand command)
        {
            if (command != null)
            {
                Executions.Add(() =>
                               {
                                   if (command.CanExecute(null))
                                       command.Execute(null);
                               });
            }

            return this;
        }

        public PropertyDependencyDefinitions Execute(RelayCommand command)
        {
            if (command != null)
                Executions.Add(command.Execute);

            return this;
        }

        public PropertyDependencyDefinitions IfCanExecute(RelayCommand command)
        {
            if (command != null)
            {
                Executions.Add(() =>
                               {
                                   if (command.CanExecute())
                                       command.Execute();
                               });
            }

            return this;
        }

        public PropertyDependencyDefinitions Execute<TParam>(RelayCommand<TParam> command)
        {
            if (command != null)
                Executions.Add(command.Execute);

            return this;
        }

        public PropertyDependencyDefinitions IfCanExecute<TParam>(RelayCommand<TParam> command)
        {
            if (command != null)
            {
                Executions.Add(() =>
                               {
                                   if (command.CanExecute())
                                       command.Execute();
                               });
            }

            return this;
        }

        public PropertyDependencyDefinitions Raise(RelayCommand command)
        {
            if (command != null)
                Executions.Add(command.RaiseCanExecuteChanged);

            return this;
        }

        public PropertyDependencyDefinitions Raise<TParam>(RelayCommand<TParam> command)
        {
            if (command != null)
                Executions.Add(command.RaiseCanExecuteChanged);

            return this;
        }

        internal void Free(string name) => List.RemoveAll(p => p.Equals(name));
    }
}
