using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using INotify.Core.Contracts;
using INotify.Core.Delegates;
using INotify.Core.Dictionaries;
using INotify.Core.EventArguments;
using INotify.Core.Extensions;
using INotify.Core.Internal;
using static System.String;

namespace INotify.Core
{
    public abstract class Notifier : INotifyPropertyChanged, INotifyEnabling
    {
        #region fields

        internal static readonly CollectionsSessionDictionary CollectionSessions = new CollectionsSessionDictionary();
        internal static readonly PropertiesSessionDictionary PropertySessions = new PropertiesSessionDictionary();
        static readonly object Locker = new object();
        static long _session;
        protected internal bool IsNotificationsEnabled = true;
        internal readonly PropertyDependenciesDictionary LocalPropertyDependencies = new PropertyDependenciesDictionary();
        internal readonly PropertyDependenciesDictionary ReferencedCollectionDependencies = new PropertyDependenciesDictionary();
        internal readonly ReferencePropertyDependenciesDictionary ReferencedCollectionItemPropertyDependencies = new ReferencePropertyDependenciesDictionary();
        internal readonly ReferencePropertyDependenciesDictionary ReferencedPropertyDependencies = new ReferencePropertyDependenciesDictionary();
        readonly PropertyReferenceDictionary<IReactToCollectionItemProperty> _collectionItemsReferenceMap = new PropertyReferenceDictionary<IReactToCollectionItemProperty>();
        readonly PropertyReferenceDictionary<INotifyCollectionChanged> _collectionReferenceMap = new PropertyReferenceDictionary<INotifyCollectionChanged>();
        readonly ConcurrentDictionary<string, object> _dependentPropertyValuesDictionary = new ConcurrentDictionary<string, object>();
        readonly List<PropertyChangedEventHandler> _propertyChangedSubscribers = new List<PropertyChangedEventHandler>();
        readonly PropertyReferenceDictionary<INotifyPropertyChanged> _propertyReferenceMap = new PropertyReferenceDictionary<INotifyPropertyChanged>();
        readonly ConcurrentDictionary<string, object> _propertyValuesDictionary = new ConcurrentDictionary<string, object>();
        readonly List<ReactToPropertyEventHandler> _reactToPropertySubscribers = new List<ReactToPropertyEventHandler>();

        #endregion

        #region constructors

        protected Notifier() => _reactToProperty += RespondToPropertyReactions;
        ~Notifier() => _reactToProperty -= RespondToPropertyReactions;

        #endregion

        #region events

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

        [SuppressMessage("ReSharper", "InconsistentNaming", Justification = "Event is Private.")]
        event PropertyChangedEventHandler _propertyChanged;

        [SuppressMessage("ReSharper", "InconsistentNaming", Justification = "Event is Private.")]
        event ReactToPropertyEventHandler _reactToProperty;

        #endregion

        #region methods

        public static string GetName<TProp>(Expression<Func<TProp>> property) => property.GetName();

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

        protected internal virtual void OnReactToProperty(long session, string propertyName)
        {
            var handler = _reactToProperty;

            if (handler == null || IsNullOrWhiteSpace(propertyName))
                return;

            var args = new ReactToPropertyEventArgs(session, propertyName);

            if (!PropertySessions.TrackReaction(this, args))
                return;

            if (_dependentPropertyValuesDictionary.TryRemove(propertyName, out var value))
                Ignore(value, propertyName);

            if (IsNotificationsEnabled)
                handler(this, args);
        }

        protected internal void RaiseDependencies(long session, string propertyName, PropertyDependenciesDictionary dependencies)
        {
            if (!dependencies.TryGetValue(propertyName, out var properties))
                return;

            HandleExecutions(properties);
            RaiseDependencies(session, properties);
        }

        protected internal void RaiseDependencies(long session, PropertyDependencyDefinitions dependencies) => dependencies.List.ForEach(property => OnReactToProperty(session, property.Name));

        protected internal virtual void RespondToCollectionItemPropertyReactions(object sender, ReactToCollectionItemPropertyEventArgs args)
        {
            if (!(sender is IReactToCollectionItemProperty))
                return;

            foreach (var reference in _collectionItemsReferenceMap.Where(reference => reference.Value.Equals(sender)))
            {
                if (ReferencedCollectionItemPropertyDependencies.TryGetValue(reference.Key, out var dependencies))
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
                if (ReferencedCollectionDependencies.TryGetValue(reference.Key, out var dependencies))
                    RaiseDependencies(args.Session, dependencies);

                break;
            }
        }

        protected PropertyDependencyDefinitions CollectionChangeFor<TColl>(Expression<Func<TColl>> property) where TColl : INotifyCollectionChanged => ReferencedCollectionDependencies.Get(property.GetName());

        protected PropertyDependencyDefinitions CollectionItemPropertyChangedFor<TColl, TItem, TProp>(Expression<Func<TColl>> reference, Expression<Func<TItem, TProp>> property)
            where TColl : IReactToCollectionItemProperty =>
            ReferencedCollectionItemPropertyDependencies.Retrieve(reference.GetName())
                                                        .Get(property.GetName());

        protected TProp GetValue<TProp>(Expression<Func<TProp>> property, Func<TProp> computed)
        {
            var propertyName = property.GetName();

            if (!IsNotificationsEnabled)
                return computed();

            if (_dependentPropertyValuesDictionary.TryGetValue(propertyName, out var value))
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
            if (_propertyValuesDictionary.TryGetValue(property.GetName(), out var value))
                return (TProp)value;

            return default;
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

        protected PropertyDependencyDefinitions PropertyChangeFor<TProp>(Expression<Func<TProp>> property) => LocalPropertyDependencies.Get(property.GetName());

        protected PropertyDependencyDefinitions PropertyChangeFor<TRef, TInst, TProp>(Expression<Func<TRef>> reference, Expression<Func<TInst, TProp>> property) where TRef : INotifyPropertyChanged
                                                                                                                                                                 where TInst : TRef =>
            ReferencedPropertyDependencies.Retrieve(reference.GetName())
                                          .Get(property.GetName());

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

        internal static void EndSession(long session)
        {
            if (CollectionSessions.TryRemove(session, out var collectionNotifications))
            {
                foreach (var cn in collectionNotifications)
                    cn.Key.OnCollectionChanged(cn.Value);
            }

            if (!PropertySessions.TryRemove(session, out var propertyNotifications))
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

        static void HandleExecutions(PropertyDependencyDefinitions propertyExecutions)
        {
            foreach (var action in propertyExecutions.Executions)
                action();
        }

        void Ignore<TProp>(TProp value, string propertyName)
        {
            switch (value)
            {
                case Notifier notifier:
                    IgnorePropertyReactionsOn(propertyName, notifier);

                    break;
                case INotifyPropertyChanged notifyPropertyChanged:
                    IgnorePropertyChangesOn(propertyName, notifyPropertyChanged);

                    break;
            }

            switch (value)
            {
                case IReactToCollection reactToCollection:
                    IgnoreCollectionReactionsOn(propertyName, reactToCollection);

                    break;
                case INotifyCollectionChanged notifyCollectionChanged:
                    IgnoreCollectionChangesOn(propertyName, notifyCollectionChanged);

                    break;
            }

            if (value is IReactToCollectionItemProperty reactToCollectionItemProperty)
                IgnoreCollectionItemPropertyReactionsOn(propertyName, reactToCollectionItemProperty);
        }

        void IgnoreCollectionChangesOn(string referenceName, INotifyCollectionChanged ignored)
        {
            if (ignored != null)
                ignored.CollectionChanged -= RespondToCollectionChanges;

            _collectionReferenceMap.Remove(referenceName);
        }

        void IgnoreCollectionItemPropertyReactionsOn(string referenceName, IReactToCollectionItemProperty ignored)
        {
            if (ignored != null)
                ignored.ReactToCollectionItemProperty -= RespondToCollectionItemPropertyReactions;

            _collectionItemsReferenceMap.Remove(referenceName);
        }

        void IgnoreCollectionReactionsOn(string referenceName, IReactToCollection ignored)
        {
            if (ignored != null)
                ignored.ReactToCollection -= RespondToCollectionReactions;

            _collectionReferenceMap.Remove(referenceName);
        }

        void IgnorePropertyChangesOn(string referenceName, INotifyPropertyChanged ignored)
        {
            if (ignored != null)
                ignored.PropertyChanged -= RespondToPropertyChanges;

            _propertyReferenceMap.Remove(referenceName);
        }

        void IgnorePropertyReactionsOn(string referenceName, Notifier ignored)
        {
            if (ignored != null)
                ignored._reactToProperty -= RespondToPropertyReactions;

            _propertyReferenceMap.Remove(referenceName);
        }

        void ListenForCollectionChangesOn(string referenceName, INotifyCollectionChanged listened)
        {
            if (listened == null)
                return;

            listened.CollectionChanged += RespondToCollectionChanges;
            _collectionReferenceMap.Add(referenceName, listened);
        }

        void ListenForCollectionItemPropertyReactionsOn(string referencedName, IReactToCollectionItemProperty listened)
        {
            if (listened == null)
                return;

            listened.ReactToCollectionItemProperty += RespondToCollectionItemPropertyReactions;
            _collectionItemsReferenceMap.Add(referencedName, listened);
        }

        void ListenForCollectionReactionsOn(string referenceName, IReactToCollection listened)
        {
            if (listened == null)
                return;

            listened.ReactToCollection += RespondToCollectionReactions;
            _collectionReferenceMap.Add(referenceName, listened);
        }

        void ListenForPropertyChangesOn(string referenceName, INotifyPropertyChanged listened)
        {
            if (listened == null)
                return;

            listened.PropertyChanged += RespondToPropertyChanges;
            _propertyReferenceMap.Add(referenceName, listened);
        }

        void ListenForPropertyReactionsOn(string referenceName, Notifier listened)
        {
            if (listened == null)
                return;

            listened._reactToProperty += RespondToPropertyReactions;
            _propertyReferenceMap.Add(referenceName, listened);
        }

        void ListenTo<TProp>(TProp value, string propertyName)
        {
            switch (value)
            {
                case Notifier notifier:
                    ListenForPropertyReactionsOn(propertyName, notifier);

                    break;
                case INotifyPropertyChanged notifyPropertyChanged:
                    ListenForPropertyChangesOn(propertyName, notifyPropertyChanged);

                    break;
            }

            switch (value)
            {
                case IReactToCollection reactToCollection:
                    ListenForCollectionReactionsOn(propertyName, reactToCollection);

                    break;
                case INotifyCollectionChanged notifyCollectionChanged:
                    ListenForCollectionChangesOn(propertyName, notifyCollectionChanged);

                    break;
            }

            if (value is IReactToCollectionItemProperty reactToCollectionItemProperty)
                ListenForCollectionItemPropertyReactionsOn(propertyName, reactToCollectionItemProperty);
        }

        void RespondToCollectionChanges(object sender, NotifyCollectionChangedEventArgs args)
        {
            if (!(sender is INotifyCollectionChanged))
                return;

            foreach (var reference in _collectionReferenceMap.Where(reference => reference.Value.Equals(sender)))
            {
                if (ReferencedCollectionDependencies.TryGetValue(reference.Key, out var dependencies))
                {
                    var session = StartSession();

                    RaiseDependencies(session, dependencies);
                    EndSession(session);
                }

                break;
            }
        }

        void RespondToPropertyChanges(object sender, PropertyChangedEventArgs args)
        {
            if (!(sender is INotifyPropertyChanged))
                return;

            foreach (var reference in _propertyReferenceMap.Where(reference => reference.Value.Equals(sender)))
            {
                if (ReferencedPropertyDependencies.TryGetValue(reference.Key, out var dependencies))
                {
                    var session = StartSession();

                    RaiseDependencies(session, args.PropertyName, dependencies);
                    EndSession(session);
                }

                break;
            }
        }

        void RespondToPropertyReactions(object sender, ReactToPropertyEventArgs args)
        {
            if (sender == this)
                RaiseDependencies(args.Session, args.PropertyName, LocalPropertyDependencies);
            else if (sender is Notifier notifier)
            {
                foreach (var reference in _propertyReferenceMap.Where(reference => reference.Value.Equals(notifier)))
                {
                    if (ReferencedPropertyDependencies.TryGetValue(reference.Key, out var dependencies))
                        RaiseDependencies(args.Session, args.PropertyName, dependencies);

                    break;
                }
            }
        }

        #endregion

        #region nested types

        protected class PropertyDependencyMapper
        {
            #region fields

            readonly string _dependentPropertyName;
            readonly Notifier _notifier;

            #endregion

            #region constructors

            protected internal PropertyDependencyMapper(string dependentPropertyName, Notifier notifier)
            {
                _notifier = notifier;
                _dependentPropertyName = dependentPropertyName;
            }

            #endregion

            #region methods

            public PropertyDependencyMapper DependsOnCollection<TColl>(Expression<Func<TColl>> property) where TColl : INotifyCollectionChanged
            {
                _notifier.LocalPropertyDependencies.Get(property.GetName())
                         .Affects(_dependentPropertyName);

                _notifier.ReferencedCollectionDependencies.Get(property.GetName())
                         .Affects(_dependentPropertyName);

                return this;
            }

            public PropertyDependencyMapper DependsOnCollectionItemProperty<TColl, TItem, TProp>(Expression<Func<TColl>> reference, Expression<Func<TItem, TProp>> property) where TColl : IReactToCollectionItemProperty
            {
                _notifier.LocalPropertyDependencies.Get(reference.GetName())
                         .Affects(_dependentPropertyName);

                _notifier.ReferencedCollectionDependencies.Get(reference.GetName())
                         .Affects(_dependentPropertyName);

                _notifier.ReferencedCollectionItemPropertyDependencies.Retrieve(reference.GetName())
                         .Get(property.GetName())
                         .Affects(_dependentPropertyName);

                return this;
            }

            public PropertyDependencyMapper DependsOnProperty<TProp>(Expression<Func<TProp>> property)
            {
                _notifier.LocalPropertyDependencies.Get(property.GetName())
                         .Affects(_dependentPropertyName);

                return this;
            }

            public PropertyDependencyMapper DependsOnReferenceProperty<TRef, TInst, TProp>(Expression<Func<TRef>> reference, Expression<Func<TInst, TProp>> property) where TRef : INotifyPropertyChanged
                                                                                                                                                                      where TInst : TRef
            {
                _notifier.LocalPropertyDependencies.Get(reference.GetName())
                         .Affects(_dependentPropertyName);

                _notifier.ReferencedPropertyDependencies.Retrieve(reference.GetName())
                         .Get(property.GetName())
                         .Affects(_dependentPropertyName);

                return this;
            }

            public PropertyDependencyMapper OverridesWithoutBaseReference()
            {
                foreach (var dependency in _notifier.LocalPropertyDependencies.Concat(_notifier.ReferencedPropertyDependencies.SelectMany(referencedDependencies => referencedDependencies.Value))
                                                    .Concat(_notifier.ReferencedCollectionDependencies)
                                                    .Concat(_notifier.ReferencedCollectionItemPropertyDependencies.SelectMany(referencedDependencies => referencedDependencies.Value))
                                                    .Select(kvp => kvp.Value))
                    dependency.Free(_dependentPropertyName);

                return this;
            }

            #endregion
        }

        #endregion
    }
}
