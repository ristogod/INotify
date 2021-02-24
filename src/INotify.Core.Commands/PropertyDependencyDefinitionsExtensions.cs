using System.Windows.Input;
using INotify.Core.Internal;

namespace INotify.Core.Commands
{
    public static class PropertyDependencyDefinitionsExtensions
    {
        #region methods

        public static PropertyDependencyDefinitions Execute(this PropertyDependencyDefinitions propertyDependencyDefinitions, ICommand command)
        {
            if (command is not null)
                propertyDependencyDefinitions.Executions.Add(() => command.Execute(null));

            return propertyDependencyDefinitions;
        }

        public static PropertyDependencyDefinitions Execute(this PropertyDependencyDefinitions propertyDependencyDefinitions, RelayCommand command)
        {
            if (command is not null)
                propertyDependencyDefinitions.Executions.Add(command.Execute);

            return propertyDependencyDefinitions;
        }

        public static PropertyDependencyDefinitions Execute<TParam>(this PropertyDependencyDefinitions propertyDependencyDefinitions, RelayCommand<TParam> command, TParam value)
        {
            if (command is not null)
                propertyDependencyDefinitions.Executions.Add(() => command.Execute(value));

            return propertyDependencyDefinitions;
        }

        public static PropertyDependencyDefinitions IfCanExecute(this PropertyDependencyDefinitions propertyDependencyDefinitions, ICommand command)
        {
            if (command is not null)
            {
                propertyDependencyDefinitions.Executions.Add(() =>
                                                             {
                                                                 if (command.CanExecute(null))
                                                                     command.Execute(null);
                                                             });
            }

            return propertyDependencyDefinitions;
        }

        public static PropertyDependencyDefinitions IfCanExecute(this PropertyDependencyDefinitions propertyDependencyDefinitions, RelayCommand command)
        {
            if (command is not null)
            {
                propertyDependencyDefinitions.Executions.Add(() =>
                                                             {
                                                                 if (command.CanExecute())
                                                                     command.Execute();
                                                             });
            }

            return propertyDependencyDefinitions;
        }

        public static PropertyDependencyDefinitions IfCanExecute<TParam>(this PropertyDependencyDefinitions propertyDependencyDefinitions, RelayCommand<TParam> command, TParam value)
        {
            if (command is not null)
            {
                propertyDependencyDefinitions.Executions.Add(() =>
                                                             {
                                                                 if (command.CanExecute(value))
                                                                     command.Execute(value);
                                                             });
            }

            return propertyDependencyDefinitions;
        }

        public static PropertyDependencyDefinitions Raise(this PropertyDependencyDefinitions propertyDependencyDefinitions, RelayCommand command)
        {
            if (command is not null)
                propertyDependencyDefinitions.Executions.Add(command.RaiseCanExecuteChanged);

            return propertyDependencyDefinitions;
        }

        public static PropertyDependencyDefinitions Raise<TParam>(this PropertyDependencyDefinitions propertyDependencyDefinitions, RelayCommand<TParam> command)
        {
            if (command is not null)
                propertyDependencyDefinitions.Executions.Add(command.RaiseCanExecuteChanged);

            return propertyDependencyDefinitions;
        }

        #endregion
    }
}
