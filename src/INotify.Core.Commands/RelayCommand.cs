//  Original author - Josh Smith - http://msdn.microsoft.com/en-us/magazine/dd419663.aspx#id0090030

using System;
using System.Diagnostics;
using System.Windows.Input;
using static System.Windows.Input.CommandManager;

namespace INotify.Core.Commands
{
    /// <inheritdoc/>
    /// <summary>
    ///     A command whose sole purpose is to relay its functionality to other objects by invoking delegates. The default
    ///     return value for the CanExecute method is 'true'.
    /// </summary>
    public class RelayCommand<T> : ICommand
    {
        #region fields

        readonly Predicate<T> _canExecute;
        readonly Action<T> _execute;

        #endregion

        #region constructors

        /// <inheritdoc/>
        /// <summary>
        ///     Initializes a new instance of the <see cref="T:INotify.Core.Commands.RelayCommand`1"/> class and the command can
        ///     always be
        ///     executed.
        /// </summary>
        /// <param name="execute">The execution logic.</param>
        public RelayCommand(Action<T> execute) : this(execute, null) { }

        /// <summary>
        ///     Initializes a new instance of the <see cref="RelayCommand&lt;T&gt;"/> class.
        /// </summary>
        /// <param name="execute">The execution logic.</param>
        /// <param name="canExecute">The execution status logic.</param>
        public RelayCommand(Action<T> execute, Predicate<T> canExecute) => (_execute, _canExecute) = (execute ?? throw new ArgumentNullException(nameof(execute)), canExecute);

        #endregion

        #region events

        public event EventHandler CanExecuteChanged
        {
            add
            {
                if (_canExecute is not null)
                    RequerySuggested += value;
            }
            remove
            {
                if (_canExecute is not null)
                    RequerySuggested -= value;
            }
        }

        #endregion

        #region methods

        [DebuggerStepThrough]
        public bool CanExecute(object parameter = null) => _canExecute?.Invoke((T)parameter) ?? true;

        public void Execute(object parameter) => _execute((T)parameter);
        public void RaiseCanExecuteChanged() => InvalidateRequerySuggested();

        #endregion
    }

    /// <inheritdoc/>
    /// <summary>
    ///     A command whose sole purpose is to relay its functionality to other objects by invoking delegates. The default
    ///     return value for the CanExecute method is 'true'.
    /// </summary>
    public class RelayCommand : ICommand
    {
        #region fields

        readonly Func<bool> _canExecute;
        readonly Action _execute;

        #endregion

        #region constructors

        /// <inheritdoc/>
        /// <summary>
        ///     Initializes a new instance of the <see cref="T:INotify.Core.Commands.RelayCommand"/> class and the command can
        ///     always be executed.
        /// </summary>
        /// <param name="execute">The execution logic.</param>
        public RelayCommand(Action execute) : this(execute, null) { }

        /// <summary>
        ///     Initializes a new instance of the <see cref="RelayCommand"/> class.
        /// </summary>
        /// <param name="execute">The execution logic.</param>
        /// <param name="canExecute">The execution status logic.</param>
        public RelayCommand(Action execute, Func<bool> canExecute) => (_execute, _canExecute) = (execute ?? throw new ArgumentNullException(nameof(execute)), canExecute);

        #endregion

        #region events

        public event EventHandler CanExecuteChanged
        {
            add
            {
                if (_canExecute is not null)
                    RequerySuggested += value;
            }
            remove
            {
                if (_canExecute is not null)
                    RequerySuggested -= value;
            }
        }

        #endregion

        #region methods

        [DebuggerStepThrough]
        public bool CanExecute(object parameter = null) => _canExecute?.Invoke() ?? true;

        public void Execute(object parameter) => _execute();
        public void Execute() => _execute();
        public void RaiseCanExecuteChanged() => InvalidateRequerySuggested();

        #endregion
    }
}
