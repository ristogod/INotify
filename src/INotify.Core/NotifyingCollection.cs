using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using INotify.Core.Contracts;
using INotify.Core.Delegates;
using INotify.Core.Dictionaries;
using INotify.Core.EventArguments;
using INotify.Core.Extensions;
using INotify.Core.Internal;
using static System.Collections.Specialized.NotifyCollectionChangedAction;

namespace INotify.Core
{
    [DebuggerDisplay(nameof(Count) + " = {" + nameof(Count) + "}")]
    public abstract class NotifyingCollection<T> : Notifier, IList<T>, IList, IReactToCollection, IReactToCollectionItemProperty
    {
        #region fields

        const string CAPACITY = "Capacity";
        const string COUNT = "Count";
        const string FIRST_ITEM = "FirstItem";
        const string ITEM = "Item[]";
        const string LAST_ITEM = "LastItem";
        readonly List<NotifyCollectionChangedEventHandler> _collectionChangedSubscribers = new();
        readonly List<T> _list;
        readonly PropertyDependencyDefinitions _localCollectionDependencies = new();
        readonly PropertyDependenciesDictionary _localCollectionItemsDependencies = new();
        readonly SimpleMonitor _monitor;
        readonly List<ReactToCollectionItemPropertyEventHandler> _reactToCollectionItemPropertySubscribers = new();
        readonly List<ReactToCollectionEventHandler> _reactToCollectionSubscribers = new();
        readonly ConcurrentQueue<ReactToPropertyEventArgs> _suspendedPropertyReactions = new();
        bool _isReactionsSuspended;
        ReactToCollectionEventArgs? _substituteReactToCollectionEventArgs;
        ConcurrentQueue<ReactToCollectionEventArgs> _suspendedCollectionReactions = new();

        #endregion

        #region constructors

        protected NotifyingCollection()
        {
            PropertyOf(() => FirstItem)
                .DependsOnProperty(() => Count);

            PropertyOf(() => LastItem)
                .DependsOnProperty(() => Count);

            _reactToCollection += RespondToCollectionReactions;
            _reactToCollectionItemProperty += RespondToCollectionItemPropertyReactions;

            (_monitor, _list) = (new(), new());
        }

        protected NotifyingCollection(int capacity)
        {
            PropertyOf(() => FirstItem)
                .DependsOnProperty(() => Count);

            PropertyOf(() => LastItem)
                .DependsOnProperty(() => Count);

            _reactToCollection += RespondToCollectionReactions;
            _reactToCollectionItemProperty += RespondToCollectionItemPropertyReactions;

            (_monitor, _list) = (new(), new(capacity));
        }

        protected NotifyingCollection(IEnumerable<T> collection)
        {
            PropertyOf(() => FirstItem)
                .DependsOnProperty(() => Count);

            PropertyOf(() => LastItem)
                .DependsOnProperty(() => Count);

            _reactToCollection += RespondToCollectionReactions;
            _reactToCollectionItemProperty += RespondToCollectionItemPropertyReactions;

            (_monitor, _list) = (new(), new(collection));

            _list.ForEach(ListenToChild);
        }

        ~NotifyingCollection()
        {
            _reactToCollection -= RespondToCollectionReactions;
            _reactToCollectionItemProperty -= RespondToCollectionItemPropertyReactions;
        }

        #endregion

        #region events

        public event NotifyCollectionChangedEventHandler? CollectionChanged
        {
            add
            {
                if (value is null || _collectionChangedSubscribers.Contains(value))
                    return;

                _collectionChanged += value;
                _collectionChangedSubscribers.Add(value);
            }
            remove
            {
                if (value is null)
                    return;

                _collectionChanged -= value;
                _collectionChangedSubscribers.Remove(value);
            }
        }

        public event ReactToCollectionEventHandler? ReactToCollection
        {
            add
            {
                if (value is null || _reactToCollectionSubscribers.Contains(value))
                    return;

                _reactToCollection += value;
                _reactToCollectionSubscribers.Add(value);
            }
            remove
            {
                if (value is null)
                    return;

                _reactToCollection -= value;
                _reactToCollectionSubscribers.Remove(value);
            }
        }

        public event ReactToCollectionItemPropertyEventHandler? ReactToCollectionItemProperty
        {
            add
            {
                if (value is null || _reactToCollectionItemPropertySubscribers.Contains(value))
                    return;

                _reactToCollectionItemProperty += value;
                _reactToCollectionItemPropertySubscribers.Add(value);
            }
            remove
            {
                if (value is null)
                    return;

                _reactToCollectionItemProperty -= value;
                _reactToCollectionItemPropertySubscribers.Remove(value);
            }
        }

        [SuppressMessage("ReSharper", "InconsistentNaming", Justification = "Event is Private.")]
        event NotifyCollectionChangedEventHandler? _collectionChanged;

        [SuppressMessage("ReSharper", "InconsistentNaming", Justification = "Event is Private.")]
        event ReactToCollectionEventHandler? _reactToCollection;

        [SuppressMessage("ReSharper", "InconsistentNaming", Justification = "Event is Private.")]
        event ReactToCollectionItemPropertyEventHandler? _reactToCollectionItemProperty;

        #endregion

        #region properties

        public int Capacity
        {
            get => _list.Capacity;
            set
            {
                var oldValue = _list.Capacity;
                _list.Capacity = value;

                if (oldValue == value)
                    return;

                var session = StartSession();
                OnReactToProperty(session, CAPACITY);
                EndSession(session);
            }
        }

        public int Count => _list.Count;

        public T FirstItem =>
            GetValue(() => FirstItem,
                     () => Count is > 0
                               ? this[0]
                               : default!);

        public virtual T this[int index]
        {
            get => _list[index];
            set
            {
                var session = StartSession();

                CheckPropertyEnds(session,
                                  () =>
                                  {
                                      var oldValue = _list[index];
                                      _list[index] = value;

                                      IgnoreChild(oldValue);
                                      ListenToChild(value);

                                      OnReactToCollection(new(session, Replace, value, oldValue, index));
                                      OnReactToProperty(session, ITEM);
                                      OnReactToProperty(session, COUNT);
                                  });
                EndSession(session);
            }
        }

        public T LastItem =>
            GetValue(() => LastItem,
                     () => Count is > 0
                               ? this[Count - 1]
                               : default!);

        bool IList.IsFixedSize => ((IList)_list).IsFixedSize;
        bool ICollection<T>.IsReadOnly => ((ICollection<T>)_list).IsReadOnly;
        bool IList.IsReadOnly => ((IList)_list).IsReadOnly;
        bool ICollection.IsSynchronized => ((ICollection)_list).IsSynchronized;

        [SuppressMessage("ReSharper", "UncatchableException", Justification = "Wanted to provide our own specific message.")]
        object? IList.this[int index]
        {
            get => this[index];
            set
            {
                try
                {
                    this[index] = (T)value!;
                }
                catch (InvalidCastException invalidCastException)
                {
                    throw new InvalidCastException($"{value!.GetType()} cannot be converted to {typeof(T)}", invalidCastException);
                }
            }
        }

        object ICollection.SyncRoot => ((ICollection)_list).SyncRoot;

        #endregion

        #region methods

        public virtual void Add(T item)
        {
            CheckReentrancy();
            var session = StartSession();

            CheckPropertyEnds(session,
                              () =>
                              {
                                  var index = Count;
                                  _list.Add(item);

                                  ListenToChild(item);

                                  OnReactToCollection(new(session, NotifyCollectionChangedAction.Add, item, index));
                                  OnReactToProperty(session, ITEM);
                                  OnReactToProperty(session, COUNT);
                              });
            EndSession(session);
        }

        public virtual void AddRange(IEnumerable<T> collection)
        {
            if (collection is null)
                throw new ArgumentNullException(nameof(collection));

            var session = StartSession();

            GroupNotifications(new(session, Reset),
                               () =>
                               {
                                   foreach (var item in collection)
                                       QuietItemWhile(item, Add);
                               });
            EndSession(session);
        }

        public ReadOnlyCollection<T> AsReadOnly() => _list.AsReadOnly();
        public int BinarySearch(T item) => _list.BinarySearch(item);
        public int BinarySearch(T item, IComparer<T> comparer) => _list.BinarySearch(item, comparer);
        public int BinarySearch(int index, int count, T item, IComparer<T> comparer) => _list.BinarySearch(index, count, item, comparer);

        public virtual void Clear()
        {
            CheckReentrancy();
            var session = StartSession();

            CheckPropertyEnds(session,
                              () =>
                              {
                                  var oldValues = ToArray();
                                  _list.Clear();

                                  foreach (var value in oldValues)
                                      IgnoreChild(value);

                                  OnReactToCollection(new(session, Reset));
                                  OnReactToProperty(session, ITEM);
                                  OnReactToProperty(session, COUNT);
                              });
            EndSession(session);
        }

        public bool Contains(T item) => _list.Contains(item);
        public virtual void CopyTo(T[] array, int arrayIndex) => _list.CopyTo(array, arrayIndex);
        public virtual void CopyTo(T[] array) => _list.CopyTo(array);
        public virtual void CopyTo(int index, T[] array, int arrayIndex, int count) => _list.CopyTo(index, array, arrayIndex, count);
        public bool Exists(Predicate<T> match) => _list.Exists(match);
        public T? Find(Predicate<T> match) => _list.Find(match);
        public NotifyingList<T> FindAll(Predicate<T> match) => new(_list.FindAll(match));
        public int FindIndex(Predicate<T> match) => _list.FindIndex(match);
        public int FindIndex(int startIndex, Predicate<T> match) => _list.FindIndex(startIndex, match);
        public int FindIndex(int startIndex, int count, Predicate<T> match) => _list.FindIndex(startIndex, count, match);
        public T? FindLast(Predicate<T> match) => _list.FindLast(match);
        public int FindLastIndex(Predicate<T> match) => _list.FindLastIndex(match);
        public int FindLastIndex(int startIndex, Predicate<T> match) => _list.FindLastIndex(startIndex, match);
        public int FindLastIndex(int startIndex, int count, Predicate<T> match) => _list.FindLastIndex(startIndex, count, match);
        public void ForEach(Action<T> action) => _list.ForEach(action);
        public List<T>.Enumerator GetEnumerator() => _list.GetEnumerator();
        public NotifyingList<T> GetRange(int index, int count) => new(_list.GetRange(index, count));
        public int IndexOf(T item) => _list.IndexOf(item);
        public int IndexOf(T item, int index) => _list.IndexOf(item, index);
        public int IndexOf(T item, int index, int count) => _list.IndexOf(item, index, count);

        public virtual void Insert(int index, T item)
        {
            CheckReentrancy();
            var session = StartSession();

            CheckPropertyEnds(session,
                              () =>
                              {
                                  _list.Insert(index, item);

                                  ListenToChild(item);

                                  OnReactToCollection(new(session, NotifyCollectionChangedAction.Add, item, index));
                                  OnReactToProperty(session, ITEM);
                                  OnReactToProperty(session, COUNT);
                              });
            EndSession(session);
        }

        public virtual void InsertRange(int index, IEnumerable<T> collection)
        {
            if (collection is null)
                throw new ArgumentNullException(nameof(collection));

            var session = StartSession();
            var list = collection as IList<T> ?? collection.ToList();

            GroupNotifications(new(session, NotifyCollectionChangedAction.Add, list, index),
                               () =>
                               {
                                   foreach (var item in list)
                                   {
                                       var idx = index;
                                       QuietItemWhile(item, quietItem => Insert(idx, quietItem));
                                       index++;
                                   }
                               });
            EndSession(session);
        }

        public int LastIndexOf(T item) => _list.LastIndexOf(item);
        public int LastIndexOf(T item, int index) => _list.LastIndexOf(item, index);
        public int LastIndexOf(T item, int index, int count) => _list.LastIndexOf(item, index, count);

        public virtual bool Remove(T item)
        {
            CheckReentrancy();
            var session = StartSession();

            var removed = CheckPropertyEnds(session,
                                            () =>
                                            {
                                                var index = _list.IndexOf(item);

                                                if (!_list.Remove(item))
                                                    return false;

                                                IgnoreChild(item);

                                                OnReactToCollection(new(session, NotifyCollectionChangedAction.Remove, item, index));
                                                OnReactToProperty(session, ITEM);
                                                OnReactToProperty(session, COUNT);

                                                return true;
                                            });
            EndSession(session);

            return removed;
        }

        public virtual int RemoveAll(Predicate<T> match)
        {
            var removed = _list.FindAll(match);
            var count = 0;
            var session = StartSession();

            GroupNotifications(new(session, NotifyCollectionChangedAction.Remove, removed),
                               () =>
                               {
                                   removed.ForEach(item =>
                                                   {
                                                       if (Remove(item))
                                                           count++;
                                                   });
                               });
            EndSession(session);

            return count;
        }

        public virtual void RemoveAt(int index)
        {
            CheckReentrancy();
            var session = StartSession();

            CheckPropertyEnds(session,
                              () =>
                              {
                                  var item = this[index];
                                  _list.RemoveAt(index);

                                  IgnoreChild(item);

                                  OnReactToCollection(new(session, NotifyCollectionChangedAction.Remove, item, index));
                                  OnReactToProperty(session, ITEM);
                                  OnReactToProperty(session, COUNT);
                              });
            EndSession(session);
        }

        public virtual void RemoveRange(int index, int count)
        {
            if (index is < 0)
                throw new ArgumentOutOfRangeException(nameof(index));

            if (count is < 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            if (index + count > Count)
                throw new ArgumentOutOfRangeException(nameof(count));

            List<T> removed = new();

            for (var current = 0; current < count; current++)
                removed.Add(this[index + current]);

            var session = StartSession();

            GroupNotifications(new(session, NotifyCollectionChangedAction.Remove, removed),
                               () =>
                               {
                                   for (var current = 0; current < count; current++)
                                       RemoveAt(index);
                               });
            EndSession(session);
        }

        public virtual void Reverse()
        {
            CheckReentrancy();
            var session = StartSession();

            CheckPropertyEnds(session,
                              () =>
                              {
                                  _list.Reverse();

                                  OnReactToCollection(new(session, Reset));
                                  OnReactToProperty(session, ITEM);
                              });
            EndSession(session);
        }

        public virtual void Reverse(int index, int count)
        {
            if (index is < 0)
                throw new ArgumentOutOfRangeException(nameof(index));

            if (count is < 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            if (index + count > Count)
                throw new ArgumentOutOfRangeException(nameof(count));

            CheckReentrancy();
            var session = StartSession();

            CheckPropertyEnds(session,
                              () =>
                              {
                                  _list.Reverse(index, count);

                                  OnReactToCollection(new(session, Reset));
                                  OnReactToProperty(session, ITEM);
                              });
            EndSession(session);
        }

        public virtual void Sort()
        {
            CheckReentrancy();
            var session = StartSession();

            CheckPropertyEnds(session,
                              () =>
                              {
                                  _list.Sort();

                                  OnReactToCollection(new(session, Reset));
                                  OnReactToProperty(session, ITEM);
                              });
            EndSession(session);
        }

        public virtual void Sort(Comparison<T> comparison)
        {
            CheckReentrancy();
            var session = StartSession();

            CheckPropertyEnds(session,
                              () =>
                              {
                                  _list.Sort(comparison);

                                  OnReactToCollection(new(session, Reset));
                                  OnReactToProperty(session, ITEM);
                              });
            EndSession(session);
        }

        public virtual void Sort(IComparer<T> comparer)
        {
            CheckReentrancy();
            var session = StartSession();

            CheckPropertyEnds(session,
                              () =>
                              {
                                  _list.Sort(comparer);

                                  OnReactToCollection(new(session, Reset));
                                  OnReactToProperty(session, ITEM);
                              });
            EndSession(session);
        }

        public virtual void Sort(int index, int count, IComparer<T> comparer)
        {
            CheckReentrancy();
            var session = StartSession();

            CheckPropertyEnds(session,
                              () =>
                              {
                                  _list.Sort(index, count, comparer);

                                  OnReactToCollection(new(session, Reset));
                                  OnReactToProperty(session, ITEM);
                              });
            EndSession(session);
        }

        public T[] ToArray() => _list.ToArray();
        public void TrimExcess() => _list.TrimExcess();
        public bool TrueForAll(Predicate<T> match) => _list.TrueForAll(match);

        protected internal override void OnReactToProperty(long session, string? propertyName)
        {
            if (_isReactionsSuspended)
                EnqueueSuspendedNotification(new ReactToPropertyEventArgs(session, propertyName));
            else
                base.OnReactToProperty(session, propertyName);
        }

        protected internal override void RespondToCollectionItemPropertyReactions(object sender, ReactToCollectionItemPropertyEventArgs args)
        {
            if (sender == this)
                RaiseDependencies(args.Session, args.PropertyName, _localCollectionItemsDependencies);
            else if (sender is IReactToCollectionItemProperty reactToCollectionItemProperty)
                base.RespondToCollectionItemPropertyReactions(reactToCollectionItemProperty, args);
        }

        protected internal override void RespondToCollectionReactions(object sender, ReactToCollectionEventArgs args)
        {
            if (sender == this)
                RaiseDependencies(args.Session, _localCollectionDependencies);
            else if (sender is IReactToCollection reactToCollection)
                base.RespondToCollectionReactions(reactToCollection, args);
        }

        protected IDisposable BlockReentrancy()
        {
            _monitor.Enter();

            return _monitor;
        }

        protected void CheckReentrancy()
        {
            if (_monitor.Busy
                && _collectionChanged?.GetInvocationList()
                                     .Length is > 1)
                throw new InvalidOperationException("Collection Reentrancy Not Allowed");
        }

        protected PropertyDependencyDefinitions CollectionChange() => _localCollectionDependencies;
        protected PropertyDependencyDefinitions CollectionItemPropertyChangedFor<TItem, TProp>(Expression<Func<TItem, TProp>> property) => _localCollectionItemsDependencies.Get(property.GetName());

        protected void GroupNotifications(ReactToCollectionEventArgs substitution, Action action)
        {
            _substituteReactToCollectionEventArgs = substitution;
            GroupNotifications(action);
            _substituteReactToCollectionEventArgs = null;
        }

        protected void GroupNotifications(Action action)
        {
            _isReactionsSuspended = true;

            action();

            _isReactionsSuspended = false;

            if (_substituteReactToCollectionEventArgs is not null)
            {
                _suspendedCollectionReactions = new();
                _suspendedCollectionReactions.Enqueue(_substituteReactToCollectionEventArgs);

                foreach (var pc in _suspendedPropertyReactions)
                    pc.Session = _substituteReactToCollectionEventArgs.Session;
            }

            DequeueSuspendedNotifications(_suspendedCollectionReactions, OnReactToCollection);
            DequeueSuspendedNotifications(_suspendedPropertyReactions, OnReactToProperty);
        }

        protected new PropertyDependencyMapper PropertyOf<TProp>(Expression<Func<TProp>> property) => new(property.GetName(), this);

        static void DequeueSuspendedNotifications<TQ>(ConcurrentQueue<TQ> queue, Action<TQ> notifier)
        {
            while (queue.Count is > 0)
            {
                if (queue.TryDequeue(out var outValue))
                    notifier(outValue);
            }
        }

        [SuppressMessage("ReSharper", "ConvertIfStatementToSwitchStatement", Justification = "Unnecessary Complication")]
        static bool Equals(T? a, T? b) => a is null && b is null || a is not null && b is not null && a.Equals(b);

        static void QuietItemWhile(T item, Action<T> action)
        {
            if (item is INotifyEnabling notifyEnabling)
            {
                notifyEnabling.DisableNotifications();
                action(item);
                notifyEnabling.EnableNotifications();
            }
            else
                action(item);
        }

        int IList.Add(object? item)
        {
            var index = -1;

            if (item is not T t)
                return index;

            CheckReentrancy();
            var session = StartSession();

            CheckPropertyEnds(session,
                              () =>
                              {
                                  index = Count;
                                  _list.Add(t);

                                  ListenToChild(t);

                                  OnReactToCollection(new(session, NotifyCollectionChangedAction.Add, item, index));
                                  OnReactToProperty(session, ITEM);
                                  OnReactToProperty(session, COUNT);
                              });
            EndSession(session);

            return index;
        }

        void CheckPropertyEnds(long session, Action action)
        {
            var firstItem = default(T);
            var lastItem = default(T);

            if (Count is > 0)
            {
                firstItem = this[0];
                lastItem = this[Count - 1];
            }

            action();

            if (!Equals(FirstItem, firstItem))
                OnReactToProperty(session, FIRST_ITEM);

            if (!Equals(LastItem, lastItem))
                OnReactToProperty(session, LAST_ITEM);
        }

        TReturn CheckPropertyEnds<TReturn>(long session, Func<TReturn> function)
        {
            var firstItem = default(T);
            var lastItem = default(T);

            if (Count is > 0)
            {
                firstItem = this[0];
                lastItem = this[Count - 1];
            }

            var r = function();

            if (!Equals(FirstItem, firstItem))
                OnReactToProperty(session, FIRST_ITEM);

            if (!Equals(LastItem, lastItem))
                OnReactToProperty(session, LAST_ITEM);

            return r;
        }

        bool IList.Contains(object? item) => ((IList)_list).Contains(item);
        void ICollection.CopyTo(Array array, int arrayIndex) => ((ICollection)_list).CopyTo(array, arrayIndex);

        void EnqueueSuspendedNotification(ReactToPropertyEventArgs suspended)
        {
            if (_suspendedPropertyReactions.Any(item => item.PropertyName?.Equals(suspended.PropertyName) ?? suspended.PropertyName is null))
                return;

            _suspendedPropertyReactions.Enqueue(suspended);
        }

        void EnqueueSuspendedNotification(ReactToCollectionEventArgs suspended) => _suspendedCollectionReactions.Enqueue(suspended);
        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)_list).GetEnumerator();
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IEnumerable<T>)_list).GetEnumerator();

        void IgnoreChild(T? child)
        {
            switch (child)
            {
                case Notifier notifier:
                    IgnoreCollectionItemPropertyReactionsOn(notifier);

                    break;

                case INotifyPropertyChanged notifyPropertyChanged:
                    IgnoreCollectionItemPropertyChangesOn(notifyPropertyChanged);

                    break;
            }
        }

        void IgnoreCollectionItemPropertyChangesOn(INotifyPropertyChanged? listened)
        {
            if (listened is not null)
                listened.PropertyChanged -= RespondToChildPropertyReactions;
        }

        void IgnoreCollectionItemPropertyReactionsOn(Notifier? listened)
        {
            if (listened is not null)
                listened.ReactToProperty -= RespondToChildPropertyReactions;
        }

        int IList.IndexOf(object? item) => ((IList)_list).IndexOf(item);
        int IList<T>.IndexOf(T item) => ((IList<T>)_list).IndexOf(item);
        void IList.Insert(int index, object? item) => Insert(index, (T)item!);

        void ListenForCollectionItemPropertyChangesOn(INotifyPropertyChanged? listened)
        {
            if (listened is not null)
                listened.PropertyChanged += RespondToChildPropertyReactions;
        }

        void ListenForCollectionItemPropertyReactionsOn(Notifier? listened)
        {
            if (listened is not null)
                listened.ReactToProperty += RespondToChildPropertyReactions;
        }

        void ListenToChild(T child)
        {
            switch (child)
            {
                case Notifier notifier:
                    ListenForCollectionItemPropertyReactionsOn(notifier);

                    break;

                case INotifyPropertyChanged notifyPropertyChanged:
                    ListenForCollectionItemPropertyChangesOn(notifyPropertyChanged);

                    break;
            }
        }

        void IReactToCollection.OnCollectionChanged(NotifyCollectionChangedEventArgs args)
        {
            if (!IsNotificationsEnabled)
                return;

            using var _ = BlockReentrancy();

            _collectionChanged?.Invoke(this, args);
        }

        void OnCollectionItemPropertyReaction(ReactToCollectionItemPropertyEventArgs? args)
        {
            if (args is not null && IsNotificationsEnabled)
                _reactToCollectionItemProperty?.Invoke(this, args);
        }

        void OnReactToCollection(ReactToCollectionEventArgs args)
        {
            if (_isReactionsSuspended)
                EnqueueSuspendedNotification(args);
            else
            {
                CollectionSessions.TrackReaction(this, args);

                if (!IsNotificationsEnabled)
                    return;

                using var _ = BlockReentrancy();

                _reactToCollection?.Invoke(this, args);
            }
        }

        void OnReactToProperty(ReactToPropertyEventArgs args) => OnReactToProperty(args.Session, args.PropertyName);

        void IList.Remove(object? item)
        {
            if (item is null)
                return;

            Remove((T)item);
        }

        void RespondToChildPropertyReactions(object? sender, PropertyChangedEventArgs args)
        {
            var session = StartSession();
            OnCollectionItemPropertyReaction(new(sender as INotifyPropertyChanged, new(session, args.PropertyName)));
            EndSession(session);
        }

        void RespondToChildPropertyReactions(object? sender, ReactToPropertyEventArgs args) => OnCollectionItemPropertyReaction(new(sender as Notifier, args));

        #endregion

        #region nested types

        protected new class PropertyDependencyMapper
        {
            #region fields

            readonly string _dependentPropertyName;
            readonly NotifyingCollection<T> _notifyingCollection;

            #endregion

            #region constructors

            protected internal PropertyDependencyMapper(string dependentPropertyName, NotifyingCollection<T> notifyingCollection) =>
                (_notifyingCollection, _dependentPropertyName) = (notifyingCollection, dependentPropertyName);

            #endregion

            #region methods

            public PropertyDependencyMapper DependsOnCollection<TColl>(Expression<Func<TColl>> property) where TColl : INotifyCollectionChanged
            {
                _notifyingCollection.LocalPropertyDependencies.Get(property.GetName())
                                    .Affects(_dependentPropertyName);

                _notifyingCollection.ReferencedCollectionDependencies.Get(property.GetName())
                                    .Affects(_dependentPropertyName);

                return this;
            }

            public PropertyDependencyMapper DependsOnCollectionItemProperty<TColl, TItem, TProp>(Expression<Func<TColl>> reference, Expression<Func<TItem, TProp>> property) where TColl : IReactToCollectionItemProperty
            {
                _notifyingCollection.LocalPropertyDependencies.Get(reference.GetName())
                                    .Affects(_dependentPropertyName);

                _notifyingCollection.ReferencedCollectionDependencies.Get(reference.GetName())
                                    .Affects(_dependentPropertyName);

                _notifyingCollection.ReferencedCollectionItemPropertyDependencies.Retrieve(reference.GetName())
                                    .Get(property.GetName())
                                    .Affects(_dependentPropertyName);

                return this;
            }

            public PropertyDependencyMapper DependsOnProperty<TProp>(Expression<Func<TProp>> property)
            {
                _notifyingCollection.LocalPropertyDependencies.Get(property.GetName())
                                    .Affects(_dependentPropertyName);

                return this;
            }

            public PropertyDependencyMapper DependsOnReferenceProperty<TRef, TInst, TProp>(Expression<Func<TRef>> reference, Expression<Func<TInst, TProp>> property) where TRef : INotifyPropertyChanged
                where TInst : TRef
            {
                _notifyingCollection.LocalPropertyDependencies.Get(reference.GetName())
                                    .Affects(_dependentPropertyName);

                _notifyingCollection.ReferencedPropertyDependencies.Retrieve(reference.GetName())
                                    .Get(property.GetName())
                                    .Affects(_dependentPropertyName);

                return this;
            }

            public PropertyDependencyMapper DependsOnThisCollection()
            {
                _notifyingCollection._localCollectionDependencies.Affects(_dependentPropertyName);

                return this;
            }

            public PropertyDependencyMapper DependsOnThisCollectionItemProperty<TItem, TProp>(Expression<Func<TItem, TProp>> property)
            {
                _notifyingCollection._localCollectionDependencies.Affects(_dependentPropertyName);

                _notifyingCollection._localCollectionItemsDependencies.Get(property.GetName())
                                    .Affects(_dependentPropertyName);

                return this;
            }

            public PropertyDependencyMapper OverridesWithoutBaseReference()
            {
                foreach (var dependency in _notifyingCollection.LocalPropertyDependencies.Concat(_notifyingCollection.ReferencedPropertyDependencies.SelectMany(referencedDependencies => referencedDependencies.Value))
                                                               .Concat(_notifyingCollection.ReferencedCollectionDependencies)
                                                               .Concat(_notifyingCollection.ReferencedCollectionItemPropertyDependencies.SelectMany(referencedDependencies => referencedDependencies.Value))
                                                               .Concat(_notifyingCollection._localCollectionItemsDependencies)
                                                               .Select(kvp => kvp.Value)
                                                               .Concat(new[]
                                                                       {
                                                                           _notifyingCollection._localCollectionDependencies
                                                                       }))
                    dependency.Free(_dependentPropertyName);

                return this;
            }

            #endregion
        }

        sealed class SimpleMonitor : IDisposable
        {
            #region fields

            int _busyCount;

            #endregion

            #region properties

            public bool Busy => _busyCount is > 0;

            #endregion

            #region methods

            public void Dispose() => _busyCount -= 1;
            public void Enter() => _busyCount += 1;

            #endregion
        }

        #endregion
    }
}
