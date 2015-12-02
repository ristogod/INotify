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
using System.Xml.Serialization;
using INotify.Contracts;
using INotify.Delegates;
using INotify.Dictionaries;
using INotify.EventArguments;
using INotify.Extensions;
using INotify.Internal;
using static System.Collections.Specialized.NotifyCollectionChangedAction;

namespace INotify
{
    [DebuggerDisplay("Count = {Count}")]
    [Serializable]
    public abstract class NotifyingCollection<T> : Notifier, IList<T>, IList, IReactToCollection, IReactToCollectionItemProperty
    {
        private const string CAPACITY = "Capacity";
        private const string COUNT = "Count";
        private const string FIRSTITEM = "FirstItem";
        private const string ITEM = "Item[]";
        private const string LASTITEM = "LastItem";
        private readonly List<NotifyCollectionChangedEventHandler> _collectionChangedSubscribers = new List<NotifyCollectionChangedEventHandler>();
        private readonly List<T> _list;
        private readonly PropertyDependencyDefinitions _localCollectionDependencies = new PropertyDependencyDefinitions();
        private readonly PropertyDependenciesDictionary _localCollectionItemsDependencies = new PropertyDependenciesDictionary();
        private readonly List<ReactToCollectionItemPropertyEventHandler> _reactToCollectionItemPropertySubscribers = new List<ReactToCollectionItemPropertyEventHandler>();
        private readonly List<ReactToCollectionEventHandler> _reactToCollectionSubscribers = new List<ReactToCollectionEventHandler>();
        private readonly ConcurrentQueue<ReactToPropertyEventArgs> _suspendedPropertyReactions = new ConcurrentQueue<ReactToPropertyEventArgs>();
        private bool _isReactionsSuspended;
        private SimpleMonitor _monitor;
        private ReactToCollectionEventArgs _substituteReactToCollectionEventArgs;
        private ConcurrentQueue<ReactToCollectionEventArgs> _suspendedCollectionReactions = new ConcurrentQueue<ReactToCollectionEventArgs>();

        protected NotifyingCollection()
        {
            Construct();
            _list = new List<T>();
        }

        protected NotifyingCollection(int capacity)
        {
            Construct();
            _list = new List<T>(capacity);
        }

        protected NotifyingCollection(IEnumerable<T> collection)
        {
            Construct();
            _list = new List<T>(collection);
            _list.ForEach(ListenToChild);
        }

        [XmlIgnore]
        public int Capacity
        {
            get { return _list.Capacity; }
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

        public T FirstItem => GetValue(() => FirstItem, () => Count > 0 ? this[0] : default(T));
        public T LastItem => GetValue(() => LastItem, () => Count > 0 ? this[Count - 1] : default(T));
        void ICollection.CopyTo(Array array, int arrayIndex) => ((ICollection)_list).CopyTo(array, arrayIndex);
        bool ICollection.IsSynchronized => ((ICollection)_list).IsSynchronized;
        object ICollection.SyncRoot => ((ICollection)_list).SyncRoot;

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

                                  OnReactToCollection(new ReactToCollectionEventArgs(session, NotifyCollectionChangedAction.Add, item, index));
                                  OnReactToProperty(session, ITEM);
                                  OnReactToProperty(session, COUNT);
                              });
            EndSession(session);
        }

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

                                  OnReactToCollection(new ReactToCollectionEventArgs(session, Reset));
                                  OnReactToProperty(session, ITEM);
                                  OnReactToProperty(session, COUNT);
                              });
            EndSession(session);
        }

        public bool Contains(T item) => _list.Contains(item);
        public virtual void CopyTo(T[] array, int arrayIndex) => _list.CopyTo(array, arrayIndex);
        public int Count => _list.Count;

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

                                                OnReactToCollection(new ReactToCollectionEventArgs(session, NotifyCollectionChangedAction.Remove, item, index));
                                                OnReactToProperty(session, ITEM);
                                                OnReactToProperty(session, COUNT);
                                                return true;
                                            });
            EndSession(session);
            return removed;
        }

        bool ICollection<T>.IsReadOnly => ((ICollection<T>)_list).IsReadOnly;
        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)_list).GetEnumerator();
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IEnumerable<T>)_list).GetEnumerator();

        int IList.Add(object item)
        {
            var index = -1;
            if (!(item is T))
                return index;

            var t = (T)item;
            CheckReentrancy();
            var session = StartSession();
            CheckPropertyEnds(session,
                              () =>
                              {
                                  index = Count;
                                  _list.Add(t);

                                  ListenToChild(t);

                                  OnReactToCollection(new ReactToCollectionEventArgs(session, NotifyCollectionChangedAction.Add, item, index));
                                  OnReactToProperty(session, ITEM);
                                  OnReactToProperty(session, COUNT);
                              });
            EndSession(session);

            return index;
        }

        bool IList.Contains(object item) => ((IList)_list).Contains((T)item);
        int IList.IndexOf(object item) => ((IList)_list).IndexOf((T)item);
        void IList.Insert(int index, object item) => Insert(index, (T)item);
        bool IList.IsFixedSize => ((IList)_list).IsFixedSize;
        bool IList.IsReadOnly => ((IList)_list).IsReadOnly;

        object IList.this[int index]
        {
            get { return this[index]; }
            set
            {
                try
                {
                    this[index] = (T)value;
                }
                catch (InvalidCastException invalidCastException)
                {
                    throw new InvalidCastException($"{value.GetType()} cannot be converted to {typeof(T)}", invalidCastException);
                }
            }
        }

        void IList.Remove(object item) => Remove((T)item);

        public virtual void Insert(int index, T item)
        {
            CheckReentrancy();
            var session = StartSession();
            CheckPropertyEnds(session,
                              () =>
                              {
                                  _list.Insert(index, item);

                                  ListenToChild(item);

                                  OnReactToCollection(new ReactToCollectionEventArgs(session, NotifyCollectionChangedAction.Add, item, index));
                                  OnReactToProperty(session, ITEM);
                                  OnReactToProperty(session, COUNT);
                              });
            EndSession(session);
        }

        public virtual T this[int index]
        {
            get { return _list[index]; }
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

                                      OnReactToCollection(new ReactToCollectionEventArgs(session, Replace, value, oldValue, index));
                                      OnReactToProperty(session, ITEM);
                                      OnReactToProperty(session, COUNT);
                                  });
                EndSession(session);
            }
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

                                  OnReactToCollection(new ReactToCollectionEventArgs(session, NotifyCollectionChangedAction.Remove, item, index));
                                  OnReactToProperty(session, ITEM);
                                  OnReactToProperty(session, COUNT);
                              });
            EndSession(session);
        }

        int IList<T>.IndexOf(T item) => ((IList<T>)_list).IndexOf(item);

        public event NotifyCollectionChangedEventHandler CollectionChanged
        {
            add
            {
                if (_collectionChangedSubscribers.Contains(value))
                    return;

                _collectionChanged += value;
                _collectionChangedSubscribers.Add(value);
            }
            remove
            {
                _collectionChanged -= value;
                _collectionChangedSubscribers.Remove(value);
            }
        }

        public event ReactToCollectionEventHandler ReactToCollection
        {
            add
            {
                if (_reactToCollectionSubscribers.Contains(value))
                    return;

                _reactToCollection += value;
                _reactToCollectionSubscribers.Add(value);
            }
            remove
            {
                _reactToCollection -= value;
                _reactToCollectionSubscribers.Remove(value);
            }
        }

        void IReactToCollection.OnCollectionChanged(NotifyCollectionChangedEventArgs args)
        {
            if (!IsNotificationsEnabled || args == null)
                return;

            var disposable = BlockReentrancy();
            using (disposable)
                _collectionChanged?.Invoke(this, args);
        }

        public event ReactToCollectionItemPropertyEventHandler ReactToCollectionItemProperty
        {
            add
            {
                if (_reactToCollectionItemPropertySubscribers.Contains(value))
                    return;

                _reactToCollectionItemProperty += value;
                _reactToCollectionItemPropertySubscribers.Add(value);
            }
            remove
            {
                _reactToCollectionItemProperty -= value;
                _reactToCollectionItemPropertySubscribers.Remove(value);
            }
        }

        [SuppressMessage("ReSharper", "InconsistentNaming")]
        private event NotifyCollectionChangedEventHandler _collectionChanged;

        [SuppressMessage("ReSharper", "InconsistentNaming")]
        private event ReactToCollectionItemPropertyEventHandler _reactToCollectionItemProperty;

        [SuppressMessage("ReSharper", "InconsistentNaming")]
        private event ReactToCollectionEventHandler _reactToCollection;

        public virtual void AddRange(IEnumerable<T> collection)
        {
            if (collection == null)
                throw new ArgumentNullException(nameof(collection));

            var session = StartSession();
            GroupNotifications(new ReactToCollectionEventArgs(session, Reset),
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
        public NotifyingList<TOutput> ConvertAll<TOutput>(Converter<T, TOutput> converter) => new NotifyingList<TOutput>(_list.ConvertAll(converter));
        public virtual void CopyTo(T[] array) => _list.CopyTo(array);
        public virtual void CopyTo(int index, T[] array, int arrayIndex, int count) => _list.CopyTo(index, array, arrayIndex, count);
        public bool Exists(Predicate<T> match) => _list.Exists(match);
        public T Find(Predicate<T> match) => _list.Find(match);
        public NotifyingList<T> FindAll(Predicate<T> match) => new NotifyingList<T>(_list.FindAll(match));
        public int FindIndex(Predicate<T> match) => _list.FindIndex(match);
        public int FindIndex(int startIndex, Predicate<T> match) => _list.FindIndex(startIndex, match);
        public int FindIndex(int startIndex, int count, Predicate<T> match) => _list.FindIndex(startIndex, count, match);
        public T FindLast(Predicate<T> match) => _list.FindLast(match);
        public int FindLastIndex(Predicate<T> match) => _list.FindLastIndex(match);
        public int FindLastIndex(int startIndex, Predicate<T> match) => _list.FindLastIndex(startIndex, match);
        public int FindLastIndex(int startIndex, int count, Predicate<T> match) => _list.FindLastIndex(startIndex, count, match);
        public void ForEach(Action<T> action) => _list.ForEach(action);
        public List<T>.Enumerator GetEnumerator() => _list.GetEnumerator();
        public NotifyingList<T> GetRange(int index, int count) => new NotifyingList<T>(_list.GetRange(index, count));
        public int IndexOf(T item) => _list.IndexOf(item);
        public int IndexOf(T item, int index) => _list.IndexOf(item, index);
        public int IndexOf(T item, int index, int count) => _list.IndexOf(item, index, count);

        public virtual void InsertRange(int index, IEnumerable<T> collection)
        {
            if (collection == null)
                throw new ArgumentNullException(nameof(collection));

            var session = StartSession();
            var list = collection as IList<T> ?? collection.ToList();
            GroupNotifications(new ReactToCollectionEventArgs(session, NotifyCollectionChangedAction.Add, list, index),
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

        public virtual int RemoveAll(Predicate<T> match)
        {
            var removed = _list.FindAll(match);
            var count = 0;
            var session = StartSession();
            GroupNotifications(new ReactToCollectionEventArgs(session, NotifyCollectionChangedAction.Remove, removed),
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

        public virtual void RemoveRange(int index, int count)
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index));

            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            if (index + count > Count)
                throw new ArgumentOutOfRangeException(nameof(count));

            var removed = new List<T>();
            for (var current = 0; current < count; current++)
                removed.Add(this[index + current]);

            var session = StartSession();
            GroupNotifications(new ReactToCollectionEventArgs(session, NotifyCollectionChangedAction.Remove, removed),
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

                                  OnReactToCollection(new ReactToCollectionEventArgs(session, Reset));
                                  OnReactToProperty(session, ITEM);
                              });
            EndSession(session);
        }

        public virtual void Reverse(int index, int count)
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index));

            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            if (index + count > Count)
                throw new ArgumentOutOfRangeException(nameof(count));

            CheckReentrancy();
            var session = StartSession();
            CheckPropertyEnds(session,
                              () =>
                              {
                                  _list.Reverse(index, count);

                                  OnReactToCollection(new ReactToCollectionEventArgs(session, Reset));
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

                                  OnReactToCollection(new ReactToCollectionEventArgs(session, Reset));
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

                                  OnReactToCollection(new ReactToCollectionEventArgs(session, Reset));
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

                                  OnReactToCollection(new ReactToCollectionEventArgs(session, Reset));
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

                                  OnReactToCollection(new ReactToCollectionEventArgs(session, Reset));
                                  OnReactToProperty(session, ITEM);
                              });
            EndSession(session);
        }

        public T[] ToArray() => _list.ToArray();
        public void TrimExcess() => _list.TrimExcess();
        public bool TrueForAll(Predicate<T> match) => _list.TrueForAll(match);

        protected internal override void OnReactToProperty(long session, string propertyName)
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
            else if (sender is IReactToCollectionItemProperty)
                base.RespondToCollectionItemPropertyReactions(sender, args);
        }

        protected internal override void RespondToCollectionReactions(object sender, ReactToCollectionEventArgs args)
        {
            if (sender == this)
                RaiseDependencies(args.Session, _localCollectionDependencies);
            else if (sender is IReactToCollection)
                base.RespondToCollectionReactions(sender, args);
        }

        protected IDisposable BlockReentrancy()
        {
            _monitor.Enter();
            return _monitor;
        }

        protected void CheckReentrancy()
        {
            if (!_monitor.Busy || _collectionChanged == null || _collectionChanged.GetInvocationList().Length <= 1)
                return;

            throw new InvalidOperationException("Collection Reentrancy Not Allowed");
        }

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

            if (_substituteReactToCollectionEventArgs != null)
            {
                _suspendedCollectionReactions = new ConcurrentQueue<ReactToCollectionEventArgs>();
                _suspendedCollectionReactions.Enqueue(_substituteReactToCollectionEventArgs);
                foreach (var pc in _suspendedPropertyReactions)
                    pc.Session = _substituteReactToCollectionEventArgs.Session;
            }

            DequeueSuspendedNotifications(_suspendedCollectionReactions, OnReactToCollection);
            DequeueSuspendedNotifications(_suspendedPropertyReactions, OnReactToProperty);
        }

        protected new PropertyDependencyMapper PropertyOf<TProp>(Expression<Func<TProp>> property) => new PropertyDependencyMapper(property.GetName(), this);
        protected PropertyDependencyDefinitions CollectionChange() => _localCollectionDependencies;
        protected PropertyDependencyDefinitions CollectionItemPropertyChangedFor<TItem, TProp>(Expression<Func<TItem, TProp>> property) => _localCollectionItemsDependencies.Get(property.GetName());

        private void CheckPropertyEnds(long session, Action action)
        {
            var firstItem = default(T);
            var lastItem = default(T);

            if (Count > 0)
            {
                firstItem = this[0];
                lastItem = this[Count - 1];
            }

            action();

            if (!Equals(FirstItem, firstItem))
                OnReactToProperty(session, FIRSTITEM);

            if (!Equals(LastItem, lastItem))
                OnReactToProperty(session, LASTITEM);
        }

        private TReturn CheckPropertyEnds<TReturn>(long session, Func<TReturn> function)
        {
            var firstItem = default(T);
            var lastItem = default(T);

            if (Count > 0)
            {
                firstItem = this[0];
                lastItem = this[Count - 1];
            }

            var r = function();

            if (!Equals(FirstItem, firstItem))
                OnReactToProperty(session, FIRSTITEM);

            if (!Equals(LastItem, lastItem))
                OnReactToProperty(session, LASTITEM);

            return r;
        }

        private void Construct()
        {
            PropertyOf(() => FirstItem).DependsOnProperty(() => Count);
            PropertyOf(() => LastItem).DependsOnProperty(() => Count);

            _monitor = new SimpleMonitor();
            _reactToCollection += RespondToCollectionReactions;
            _reactToCollectionItemProperty += RespondToCollectionItemPropertyReactions;
        }

        private static void DequeueSuspendedNotifications<TQ>(ConcurrentQueue<TQ> queue, Action<TQ> notifier)
        {
            while (queue.Count > 0)
            {
                TQ outValue;
                if (queue.TryDequeue(out outValue))
                    notifier(outValue);
            }
        }

        private void EnqueueSuspendedNotification(ReactToPropertyEventArgs suspended)
        {
            if (_suspendedPropertyReactions.Any(item => item.PropertyName.Equals(suspended.PropertyName)))
                return;

            _suspendedPropertyReactions.Enqueue(suspended);
        }

        private void EnqueueSuspendedNotification(ReactToCollectionEventArgs suspended) => _suspendedCollectionReactions.Enqueue(suspended);

        private static bool Equals(T a, T b)
        {
            if (a == null && b == null)
                return true;

            if (a == null)
                return false;

            return b != null && a.Equals(b);
        }

        private void IgnoreChild(T child)
        {
            if (child is Notifier)
                IgnoreCollectionItemPropertyReactionsOn(child as Notifier);
            else if (child is INotifyPropertyChanged)
                IgnoreCollectionItemPropertyChangesOn(child as INotifyPropertyChanged);
        }

        private void IgnoreCollectionItemPropertyChangesOn(INotifyPropertyChanged listened)
        {
            if (listened != null)
                listened.PropertyChanged -= RespondToChildPropertyReactions;
        }

        private void IgnoreCollectionItemPropertyReactionsOn(Notifier listened)
        {
            if (listened != null)
                listened.ReactToProperty -= RespondToChildPropertyReactions;
        }

        private void ListenForCollectionItemPropertyChangesOn(INotifyPropertyChanged listened)
        {
            if (listened != null)
                listened.PropertyChanged += RespondToChildPropertyReactions;
        }

        private void ListenForCollectionItemPropertyReactionsOn(Notifier listened)
        {
            if (listened != null)
                listened.ReactToProperty += RespondToChildPropertyReactions;
        }

        private void ListenToChild(T child)
        {
            if (child is Notifier)
                ListenForCollectionItemPropertyReactionsOn(child as Notifier);
            else if (child is INotifyPropertyChanged)
                ListenForCollectionItemPropertyChangesOn(child as INotifyPropertyChanged);
        }

        private void OnCollectionItemPropertyReaction(ReactToCollectionItemPropertyEventArgs args)
        {
            if (args != null && IsNotificationsEnabled)
                _reactToCollectionItemProperty?.Invoke(this, args);
        }

        private void OnReactToCollection(ReactToCollectionEventArgs args)
        {
            if (_isReactionsSuspended)
                EnqueueSuspendedNotification(args);
            else
            {
                if (args == null)
                    return;

                CollectionSessions.TrackReaction(this, args);

                if (!IsNotificationsEnabled)
                    return;

                var disposable = BlockReentrancy();
                using (disposable)
                    _reactToCollection?.Invoke(this, args);
            }
        }

        private void OnReactToProperty(ReactToPropertyEventArgs args) => OnReactToProperty(args.Session, args.PropertyName);

        private static void QuietItemWhile(T item, Action<T> action)
        {
            var notifyEnabling = item as INotifyEnabling;
            if (notifyEnabling != null)
            {
                notifyEnabling.DisableNotifications();
                action(item);
                notifyEnabling.EnableNotifications();
            }
            else
                action(item);
        }

        private void RespondToChildPropertyReactions(object sender, PropertyChangedEventArgs args)
        {
            var session = StartSession();
            OnCollectionItemPropertyReaction(new ReactToCollectionItemPropertyEventArgs(sender as INotifyPropertyChanged, new ReactToPropertyEventArgs(session, args.PropertyName)));
            EndSession(session);
        }

        private void RespondToChildPropertyReactions(object sender, ReactToPropertyEventArgs args) => OnCollectionItemPropertyReaction(new ReactToCollectionItemPropertyEventArgs(sender as Notifier, args));

        protected new class PropertyDependencyMapper
        {
            private readonly string _dependentPropertyName;
            private readonly NotifyingCollection<T> _notifyingCollection;

            protected internal PropertyDependencyMapper(string dependentPropertyName, NotifyingCollection<T> notifyingCollection)
            {
                _notifyingCollection = notifyingCollection;
                _dependentPropertyName = dependentPropertyName;
            }

            public PropertyDependencyMapper DependsOnProperty<TProp>(Expression<Func<TProp>> property)
            {
                _notifyingCollection.LocalPropertyDependencies.Get(property.GetName()).Affects(_dependentPropertyName);
                return this;
            }

            public PropertyDependencyMapper DependsOnReferenceProperty<TRef, TInst, TProp>(Expression<Func<TRef>> reference, Expression<Func<TInst, TProp>> property) where TRef : INotifyPropertyChanged where TInst : TRef
            {
                _notifyingCollection.LocalPropertyDependencies.Get(reference.GetName()).Affects(_dependentPropertyName);
                _notifyingCollection.ReferencedPropertyDependencies.Retrieve(reference.GetName()).Get(property.GetName()).Affects(_dependentPropertyName);
                return this;
            }

            public PropertyDependencyMapper DependsOnCollection<TColl>(Expression<Func<TColl>> property) where TColl : INotifyCollectionChanged
            {
                _notifyingCollection.LocalPropertyDependencies.Get(property.GetName()).Affects(_dependentPropertyName);
                _notifyingCollection.ReferencedCollectionDependencies.Get(property.GetName()).Affects(_dependentPropertyName);
                return this;
            }

            public PropertyDependencyMapper DependsOnCollectionItemProperty<TColl, TItem, TProp>(Expression<Func<TColl>> reference, Expression<Func<TItem, TProp>> property) where TColl : IReactToCollectionItemProperty
            {
                _notifyingCollection.LocalPropertyDependencies.Get(reference.GetName()).Affects(_dependentPropertyName);
                _notifyingCollection.ReferencedCollectionDependencies.Get(reference.GetName()).Affects(_dependentPropertyName);
                _notifyingCollection.ReferencedCollectionItemPropertyDependencies.Retrieve(reference.GetName()).Get(property.GetName()).Affects(_dependentPropertyName);
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
                _notifyingCollection._localCollectionItemsDependencies.Get(property.GetName()).Affects(_dependentPropertyName);
                return this;
            }

            public PropertyDependencyMapper OverridesWithoutBaseReference()
            {
                foreach (var dependency in
                    _notifyingCollection.LocalPropertyDependencies.Concat(_notifyingCollection.ReferencedPropertyDependencies.SelectMany(referencedDependencies => referencedDependencies.Value))
                                        .Concat(_notifyingCollection.ReferencedCollectionDependencies)
                                        .Concat(_notifyingCollection.ReferencedCollectionItemPropertyDependencies.SelectMany(referencedDependencies => referencedDependencies.Value))
                                        .Concat(_notifyingCollection._localCollectionItemsDependencies)
                                        .Select(kvp => kvp.Value)
                                        .Concat(new[] { _notifyingCollection._localCollectionDependencies }))
                    dependency.Free(_dependentPropertyName);

                return this;
            }
        }

        [Serializable]
        private class SimpleMonitor : IDisposable
        {
            private int _busyCount;
            public bool Busy => _busyCount > 0;

            public void Dispose()
            {
                var simpleMonitor = this;
                simpleMonitor._busyCount = simpleMonitor._busyCount - 1;
            }

            public void Enter()
            {
                var simpleMonitor = this;
                simpleMonitor._busyCount = simpleMonitor._busyCount + 1;
            }
        }
    }
}
