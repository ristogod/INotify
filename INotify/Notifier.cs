using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using INotify.Dictionaries;
using INotify.Extensions;

namespace INotify
{
    public abstract class Notifier : INotifyPropertyChanged, INotifyEnabling
    {
        internal static readonly CollectionsSessionDictionary CollectionSessions = new CollectionsSessionDictionary();
        internal static readonly PropertiesSessionDictionary PropertySessions = new PropertiesSessionDictionary();
        private static long _session;
        private static readonly object Locker = new object();
        protected internal bool IsNotificationsEnabled = true;
        internal readonly PropertyDependenciesDictionary LocalPropertyDependencies = new PropertyDependenciesDictionary();
        internal readonly PropertyDependenciesDictionary ReferencedCollectionDependencies = new PropertyDependenciesDictionary();
        internal readonly ReferencePropertyDependenciesDictionary ReferencedCollectionItemPropertyDependencies = new ReferencePropertyDependenciesDictionary();
        internal readonly ReferencePropertyDependenciesDictionary ReferencedPropertyDependencies = new ReferencePropertyDependenciesDictionary();
        private readonly PropertyReferenceDictionary<IReactToCollectionItemProperty> _collectionItemsReferenceMap = new PropertyReferenceDictionary<IReactToCollectionItemProperty>();
        private readonly PropertyReferenceDictionary<INotifyCollectionChanged> _collectionReferenceMap = new PropertyReferenceDictionary<INotifyCollectionChanged>();
        private readonly ConcurrentDictionary<string, object> _dependentPropertyValuesDictionary = new ConcurrentDictionary<string, object>();
        private readonly List<PropertyChangedEventHandler> _propertyChangedSubscribers = new List<PropertyChangedEventHandler>();
        private readonly PropertyReferenceDictionary<INotifyPropertyChanged> _propertyReferenceMap = new PropertyReferenceDictionary<INotifyPropertyChanged>();
        private readonly ConcurrentDictionary<string, object> _propertyValuesDictionary = new ConcurrentDictionary<string, object>();
        private readonly List<ReactToPropertyEventHandler> _reactToPropertySubscribers = new List<ReactToPropertyEventHandler>();

        protected Notifier()
        {
            _reactToProperty += RespondToPropertyReactions;
        }

        public void DisableNotifications()
        {
            IsNotificationsEnabled = false;
            _dependentPropertyValuesDictionary.Clear();
        }

        public void EnableNotifications()
        {
            IsNotificationsEnabled = true;
            _dependentPropertyValuesDictionary.Clear();
        }

        public event PropertyChangedEventHandler PropertyChanged
        {
            add
            {
                if (_propertyChangedSubscribers.Contains(value))
                    return;

                _propertyChanged += value;
                _propertyChangedSubscribers.Add(value);
            }
            remove
            {
                _propertyChanged -= value;
                _propertyChangedSubscribers.Remove(value);
            }
        }

        public event ReactToPropertyEventHandler ReactToProperty
        {
            add
            {
                if (_reactToPropertySubscribers.Contains(value))
                    return;

                _reactToProperty += value;
                _reactToPropertySubscribers.Add(value);
            }
            remove
            {
                _reactToProperty -= value;
                _reactToPropertySubscribers.Remove(value);
            }
        }

        [SuppressMessage("ReSharper", "InconsistentNaming")]
        private event PropertyChangedEventHandler _propertyChanged;

        [SuppressMessage("ReSharper", "InconsistentNaming")]
        private event ReactToPropertyEventHandler _reactToProperty;

        public static string GetName<TProp>(Expression<Func<TProp>> property) => property.GetName();

        internal static void EndSession(long session)
        {
            CollectionChangesDictionary collectionNotifications;
            if (CollectionSessions.TryRemove(session, out collectionNotifications))
            {
                foreach (var cn in collectionNotifications)
                    cn.Key.OnCollectionChanged(cn.Value);
            }

            ReactToPropertyDictionary propertyNotifications;
            if (!PropertySessions.TryRemove(session, out propertyNotifications))
                return;

            foreach (var propertyNotification in propertyNotifications)
            {
                var notifier = propertyNotification.Key;
                if (!notifier.IsNotificationsEnabled)
                    continue;

                foreach (var pd in propertyNotification.Value)
                    notifier.OnPropertyChanged(pd.Value);
            }
        }

        internal static long StartSession()
        {
            lock (Locker)
            {
                unchecked
                {
                    _session++;
                }
                return _session;
            }
        }

        internal void OnPropertyChanged(PropertyChangedEventArgs args) => _propertyChanged?.Invoke(this, args);

        protected internal virtual void OnReactToProperty(long session, string propertyName)
        {
            var handler = _reactToProperty;

            if (handler == null || string.IsNullOrWhiteSpace(propertyName))
                return;

            var args = new ReactToPropertyEventArgs(session, propertyName);
            PropertySessions.TrackReaction(this, args);

            object value;
            if (_dependentPropertyValuesDictionary.TryRemove(propertyName, out value))
                Ignore(value, propertyName);

            if (IsNotificationsEnabled)
                handler(this, args);
        }

        protected internal void RaiseDependencies(long session, string propertyName, PropertyDependenciesDictionary dependencies)
        {
            PropertyDependencyDefinitions properties;
            if (!dependencies.TryGetValue(propertyName, out properties))
                return;

            //TODO: move executions to queue and do after batch of property raises
            HandleExecutions(properties);
            RaiseDependencies(session, properties);
        }

        protected internal void RaiseDependencies(long session, PropertyDependencyDefinitions dependencies)
        {
            foreach (var property in dependencies.List.Where(property => property.CanRaise))
                OnReactToProperty(session, property.Name);
        }

        protected internal virtual void RespondToCollectionItemPropertyReactions(object sender, ReactToCollectionItemPropertyEventArgs args)
        {
            if (!(sender is IReactToCollectionItemProperty))
                return;

            foreach (var reference in _collectionItemsReferenceMap.Where(reference => reference.Value.Equals(sender)))
            {
                PropertyDependenciesDictionary dependencies;
                if (ReferencedCollectionItemPropertyDependencies.TryGetValue(reference.Key, out dependencies))
                    RaiseDependencies(args.Session, args.PropertyName, dependencies);

                break;
            }
        }

        protected internal virtual void RespondToCollectionReactions(object sender, ReactToCollectionEventArgs args)
        {
            if (!(sender is IReactToCollection))
                return;

            foreach (var reference in _collectionReferenceMap.Where(reference => reference.Value.Equals(sender)))
            {
                PropertyDependencyDefinitions dependencies;
                if (ReferencedCollectionDependencies.TryGetValue(reference.Key, out dependencies))
                    RaiseDependencies(args.Session, dependencies);

                break;
            }
        }

        protected PropertyDependencyDefinitions CollectionChangeFor<TColl>(Expression<Func<TColl>> property) where TColl : INotifyCollectionChanged => ReferencedCollectionDependencies.Get(property.GetName());

        protected PropertyDependencyDefinitions CollectionItemPropertyChangedFor<TColl, TItem, TProp>(Expression<Func<TColl>> reference, Expression<Func<TItem, TProp>> property) where TColl : IReactToCollectionItemProperty
            => ReferencedCollectionItemPropertyDependencies.Retrieve(reference.GetName()).Get(property.GetName());

        protected PropertyDependencyDefinitions PropertyChangeFor<TProp>(Expression<Func<TProp>> property) => LocalPropertyDependencies.Get(property.GetName());

        protected PropertyDependencyDefinitions PropertyChangeFor<TRef, TInst, TProp>(Expression<Func<TRef>> reference, Expression<Func<TInst, TProp>> property) where TRef : INotifyPropertyChanged where TInst : TRef
            => ReferencedPropertyDependencies.Retrieve(reference.GetName()).Get(property.GetName());

        protected TProp GetValue<TProp>(Expression<Func<TProp>> property, Func<TProp> computed)
        {
            var propertyName = property.GetName();

            if (!IsNotificationsEnabled)
                return computed();

            object value;
            if (_dependentPropertyValuesDictionary.TryGetValue(propertyName, out value))
                return (TProp)value;

            var v = computed();

            return (TProp)_dependentPropertyValuesDictionary.AddOrUpdate(propertyName,
                                                                         key =>
                                                                         {
                                                                             ListenTo(v, propertyName);
                                                                             return v;
                                                                         },
                                                                         (key, oldValue) =>
                                                                         {
                                                                             Ignore(oldValue, propertyName);
                                                                             ListenTo(v, propertyName);
                                                                             return v;
                                                                         });
        }

        protected TProp GetValue<TProp>(Expression<Func<TProp>> property)
        {
            object value;
            if (_propertyValuesDictionary.TryGetValue(property.GetName(), out value))
                return (TProp)value;

            return default(TProp);
        }

        protected void InitializeValue<TProp>(TProp value, Expression<Func<TProp>> property)
        {
            var propertyName = property.GetName();
            _propertyValuesDictionary.AddOrUpdate(propertyName,
                                                  key =>
                                                  {
                                                      ListenTo(value, propertyName);
                                                      return value;
                                                  },
                                                  (key, oldValue) =>
                                                  {
                                                      Ignore(oldValue, propertyName);
                                                      ListenTo(value, propertyName);
                                                      return value;
                                                  });
        }

        protected PropertyDependencyMapper PropertyOf<TProp>(Expression<Func<TProp>> property) => new PropertyDependencyMapper(property.GetName(), this);

        protected bool SetValue<TProp>(TProp value, Expression<Func<TProp>> property, bool notifyWhenUnchanged = false)
        {
            var changed = false;
            var propertyName = property.GetName();

            _propertyValuesDictionary.AddOrUpdate(propertyName,
                                                  key =>
                                                  {
                                                      ListenTo(value, propertyName);
                                                      changed = true;
                                                      return value;
                                                  },
                                                  (key, oldValue) =>
                                                  {
                                                      if (Equals(oldValue, value))
                                                          return oldValue;

                                                      Ignore(oldValue, propertyName);
                                                      ListenTo(value, propertyName);
                                                      changed = true;
                                                      return value;
                                                  });

            if (!changed && !notifyWhenUnchanged)
                return false;

            var session = StartSession();
            OnReactToProperty(session, propertyName);
            EndSession(session);

            return changed;
        }

        private static void HandleExecutions(PropertyDependencyDefinitions propertyExecutions)
        {
            foreach (var action in propertyExecutions.Executions)
                action();
        }

        private void Ignore<TProp>(TProp value, string propertyName)
        {
            if (value is Notifier)
                IgnorePropertyReactionsOn(propertyName, value as Notifier);
            else if (value is INotifyPropertyChanged)
                IgnorePropertyChangesOn(propertyName, value as INotifyPropertyChanged);

            if (value is IReactToCollection)
                IgnoreCollectionReactionsOn(propertyName, value as IReactToCollection);
            else if (value is INotifyCollectionChanged)
                IgnoreCollectionChangesOn(propertyName, value as INotifyCollectionChanged);

            if (value is IReactToCollectionItemProperty)
                IgnoreCollectionItemPropertyReactionsOn(propertyName, value as IReactToCollectionItemProperty);
        }

        private void IgnoreCollectionChangesOn(string referenceName, INotifyCollectionChanged ignored)
        {
            if (ignored != null)
                ignored.CollectionChanged -= RespondToCollectionChanges;

            _collectionReferenceMap.Remove(referenceName);
        }

        private void IgnoreCollectionItemPropertyReactionsOn(string referenceName, IReactToCollectionItemProperty ignored)
        {
            if (ignored != null)
                ignored.ReactToCollectionItemProperty -= RespondToCollectionItemPropertyReactions;

            _collectionItemsReferenceMap.Remove(referenceName);
        }

        private void IgnoreCollectionReactionsOn(string referenceName, IReactToCollection ignored)
        {
            if (ignored != null)
                ignored.ReactToCollection -= RespondToCollectionReactions;

            _collectionReferenceMap.Remove(referenceName);
        }

        private void IgnorePropertyChangesOn(string referenceName, INotifyPropertyChanged ignored)
        {
            if (ignored != null)
                ignored.PropertyChanged -= RespondToPropertyChanges;

            _propertyReferenceMap.Remove(referenceName);
        }

        private void IgnorePropertyReactionsOn(string referenceName, Notifier ignored)
        {
            if (ignored != null)
                ignored._reactToProperty -= RespondToPropertyReactions;

            _propertyReferenceMap.Remove(referenceName);
        }

        private void ListenForCollectionChangesOn(string referenceName, INotifyCollectionChanged listened)
        {
            if (listened == null)
                return;

            listened.CollectionChanged += RespondToCollectionChanges;
            _collectionReferenceMap.Add(referenceName, listened);
        }

        private void ListenForCollectionItemPropertyReactionsOn(string referencedName, IReactToCollectionItemProperty listened)
        {
            if (listened == null)
                return;

            listened.ReactToCollectionItemProperty += RespondToCollectionItemPropertyReactions;
            _collectionItemsReferenceMap.Add(referencedName, listened);
        }

        private void ListenForCollectionReactionsOn(string referenceName, IReactToCollection listened)
        {
            if (listened == null)
                return;

            listened.ReactToCollection += RespondToCollectionReactions;
            _collectionReferenceMap.Add(referenceName, listened);
        }

        private void ListenForPropertyChangesOn(string referenceName, INotifyPropertyChanged listened)
        {
            if (listened == null)
                return;

            listened.PropertyChanged += RespondToPropertyChanges;
            _propertyReferenceMap.Add(referenceName, listened);
        }

        private void ListenForPropertyReactionsOn(string referenceName, Notifier listened)
        {
            if (listened == null)
                return;

            listened._reactToProperty += RespondToPropertyReactions;
            _propertyReferenceMap.Add(referenceName, listened);
        }

        private void ListenTo<TProp>(TProp value, string propertyName)
        {
            if (value is Notifier)
                ListenForPropertyReactionsOn(propertyName, value as Notifier);
            else if (value is INotifyPropertyChanged)
                ListenForPropertyChangesOn(propertyName, value as INotifyPropertyChanged);

            if (value is IReactToCollection)
                ListenForCollectionReactionsOn(propertyName, value as IReactToCollection);
            else if (value is INotifyCollectionChanged)
                ListenForCollectionChangesOn(propertyName, value as INotifyCollectionChanged);

            if (value is IReactToCollectionItemProperty)
                ListenForCollectionItemPropertyReactionsOn(propertyName, value as IReactToCollectionItemProperty);
        }

        private void RespondToCollectionChanges(object sender, NotifyCollectionChangedEventArgs args)
        {
            if (!(sender is INotifyCollectionChanged))
                return;

            foreach (var reference in _collectionReferenceMap.Where(reference => reference.Value.Equals(sender)))
            {
                PropertyDependencyDefinitions dependencies;
                if (ReferencedCollectionDependencies.TryGetValue(reference.Key, out dependencies))
                {
                    var session = StartSession();
                    RaiseDependencies(session, dependencies);
                    EndSession(session);
                }

                break;
            }
        }

        private void RespondToPropertyChanges(object sender, PropertyChangedEventArgs args)
        {
            if (!(sender is INotifyPropertyChanged))
                return;

            foreach (var reference in _propertyReferenceMap.Where(reference => reference.Value.Equals(sender)))
            {
                PropertyDependenciesDictionary dependencies;
                if (ReferencedPropertyDependencies.TryGetValue(reference.Key, out dependencies))
                {
                    var session = StartSession();
                    RaiseDependencies(session, args.PropertyName, dependencies);
                    EndSession(session);
                }

                break;
            }
        }

        private void RespondToPropertyReactions(object sender, ReactToPropertyEventArgs args)
        {
            if (sender == this)
                RaiseDependencies(args.Session, args.PropertyName, LocalPropertyDependencies);
            else if (sender is Notifier)
            {
                foreach (var reference in _propertyReferenceMap.Where(reference => reference.Value.Equals(sender)))
                {
                    PropertyDependenciesDictionary dependencies;
                    if (ReferencedPropertyDependencies.TryGetValue(reference.Key, out dependencies))
                        RaiseDependencies(args.Session, args.PropertyName, dependencies);

                    break;
                }
            }
        }

        protected class PropertyDependencyMapper
        {
            private readonly string _dependentPropertyName;
            private readonly Notifier _notifier;

            protected internal PropertyDependencyMapper(string dependentPropertyName, Notifier notifier)
            {
                _notifier = notifier;
                _dependentPropertyName = dependentPropertyName;
            }

            public PropertyDependencyMapper DependsOnProperty<TProp>(Expression<Func<TProp>> property, Func<bool> condition = null)
            {
                _notifier.LocalPropertyDependencies.Get(property.GetName()).Affects(_dependentPropertyName, condition);
                return this;
            }

            public PropertyDependencyMapper DependsOnReferenceProperty<TRef, TInst, TProp>(Expression<Func<TRef>> reference, Expression<Func<TInst, TProp>> property, Func<bool> condition = null) where TRef : INotifyPropertyChanged
                where TInst : TRef
            {
                _notifier.ReferencedPropertyDependencies.Retrieve(reference.GetName()).Get(property.GetName()).Affects(_dependentPropertyName, condition);
                return this;
            }

            public PropertyDependencyMapper DependsOnCollection<TColl>(Expression<Func<TColl>> property, Func<bool> condition = null) where TColl : INotifyCollectionChanged
            {
                _notifier.ReferencedCollectionDependencies.Get(property.GetName()).Affects(_dependentPropertyName, condition);
                return this;
            }

            public PropertyDependencyMapper DependsOnCollectionItemProperty<TColl, TItem, TProp>(Expression<Func<TColl>> reference, Expression<Func<TItem, TProp>> property, Func<bool> condition = null)
                where TColl : IReactToCollectionItemProperty
            {
                _notifier.ReferencedCollectionItemPropertyDependencies.Retrieve(reference.GetName()).Get(property.GetName()).Affects(_dependentPropertyName, condition);
                return this;
            }

            public PropertyDependencyMapper OverridesWithoutBaseReference()
            {
                foreach (var dependency in
                    _notifier.LocalPropertyDependencies.Concat(_notifier.ReferencedPropertyDependencies.SelectMany(referencedDependencies => referencedDependencies.Value))
                             .Concat(_notifier.ReferencedCollectionDependencies)
                             .Concat(_notifier.ReferencedCollectionItemPropertyDependencies.SelectMany(referencedDependencies => referencedDependencies.Value))
                             .Select(kvp => kvp.Value))
                    dependency.Free(_dependentPropertyName);

                return this;
            }
        }
    }
}
