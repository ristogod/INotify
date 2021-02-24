#if NETSTANDARD2_1 || NETCOREAPP3_1 || NET5_0
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

        internal static readonly CollectionsSessionDictionary CollectionSessions = new();
        internal static readonly PropertiesSessionDictionary PropertySessions = new();
        static readonly object Locker = new();
        static long _session;
        protected internal bool IsNotificationsEnabled = true;
        internal readonly PropertyDependenciesDictionary LocalPropertyDependencies = new();
        internal readonly PropertyDependenciesDictionary ReferencedCollectionDependencies = new();
        internal readonly ReferencePropertyDependenciesDictionary ReferencedCollectionItemPropertyDependencies = new();
        internal readonly ReferencePropertyDependenciesDictionary ReferencedPropertyDependencies = new();
        readonly PropertyReferenceDictionary<IReactToCollectionItemProperty> _collectionItemsReferenceMap = new();
        readonly PropertyReferenceDictionary<INotifyCollectionChanged> _collectionReferenceMap = new();
        readonly ConcurrentDictionary<string, object?> _dependentPropertyValuesDictionary = new();
        readonly List<PropertyChangedEventHandler> _propertyChangedSubscribers = new();
        readonly PropertyReferenceDictionary<INotifyPropertyChanged> _propertyReferenceMap = new();
        readonly ConcurrentDictionary<string, object?> _propertyValuesDictionary = new();
        readonly List<ReactToPropertyEventHandler> _reactToPropertySubscribers = new();

        #endregion

        #region constructors

        protected Notifier() => _reactToProperty += RespondToPropertyReactions;
        ~Notifier() => _reactToProperty -= RespondToPropertyReactions;

        #endregion

        #region events

        public event PropertyChangedEventHandler? PropertyChanged
        {
            add
            {
                if (value is null || _propertyChangedSubscribers.Contains(value))
                    return;

                _propertyChanged += value;
                _propertyChangedSubscribers.Add(value);
            }
            remove
            {
                if (value is null)
                    return;

                _propertyChanged -= value;
                _propertyChangedSubscribers.Remove(value);
            }
        }

        public event ReactToPropertyEventHandler? ReactToProperty
        {
            add
            {
                if (value is null || _reactToPropertySubscribers.Contains(value))
                    return;

                _reactToProperty += value;
                _reactToPropertySubscribers.Add(value);
            }
            remove
            {
                if (value is null)
                    return;

                _reactToProperty -= value;
                _reactToPropertySubscribers.Remove(value);
            }
        }

        [SuppressMessage("ReSharper", "InconsistentNaming", Justification = "Event is Private.")]
        event PropertyChangedEventHandler? _propertyChanged;

        [SuppressMessage("ReSharper", "InconsistentNaming", Justification = "Event is Private.")]
        event ReactToPropertyEventHandler? _reactToProperty;

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

        protected internal virtual void OnReactToProperty(long session, string? propertyName)
        {
            var handler = _reactToProperty;

            if (handler is null || IsNullOrWhiteSpace(propertyName))
                return;

            ReactToPropertyEventArgs args = new(session, propertyName);

            if (!PropertySessions.TrackReaction(this, args))
                return;

            if (_dependentPropertyValuesDictionary.TryRemove(propertyName, out var value))
                Ignore(value, propertyName);

            if (IsNotificationsEnabled)
                handler(this, args);
        }

        protected internal void RaiseDependencies(long session, string? propertyName, PropertyDependenciesDictionary dependencies)
        {
            if (propertyName is null || !dependencies.TryGetValue(propertyName, out var properties))
                return;

            HandleExecutions(properties);
            RaiseDependencies(session, properties);
        }

        protected internal void RaiseDependencies(long session, PropertyDependencyDefinitions dependencies) => dependencies.List.ForEach(property => OnReactToProperty(session, property.Name));

        protected internal virtual void RespondToCollectionItemPropertyReactions(object sender, ReactToCollectionItemPropertyEventArgs args)
        {
            if (sender is not IReactToCollectionItemProperty)
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
            if (sender is not IReactToCollection)
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
                return (TProp)value!;

            var v = computed();

            return (TProp)_dependentPropertyValuesDictionary.AddOrUpdate(propertyName,
                                                                         _ =>
                                                                         {
                                                                             ListenTo(v, propertyName);

                                                                             return v;
                                                                         },
                                                                         (_, oldValue) =>
                                                                         {
                                                                             Ignore(oldValue, propertyName);
                                                                             ListenTo(v, propertyName);

                                                                             return v;
                                                                         })!;
        }

        protected TProp GetValue<TProp>(Expression<Func<TProp>> property) =>
            _propertyValuesDictionary.TryGetValue(property.GetName(), out var value)
                ? (TProp)value!
                : default!;

        protected void InitializeValue<TProp>(TProp value, Expression<Func<TProp>> property)
        {
            var propertyName = property.GetName();

            _propertyValuesDictionary.AddOrUpdate(propertyName,
                                                  _ =>
                                                  {
                                                      ListenTo(value, propertyName);

                                                      return value;
                                                  },
                                                  (_, oldValue) =>
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

        protected PropertyDependencyMapper PropertyOf<TProp>(Expression<Func<TProp>> property) => new(property.GetName(), this);

        protected bool SetValue<TProp>(TProp value, Expression<Func<TProp>> property, bool notifyWhenUnchanged = false)
        {
            var changed = false;
            var propertyName = property.GetName();

            _propertyValuesDictionary.AddOrUpdate(propertyName,
                                                  _ =>
                                                  {
                                                      ListenTo(value, propertyName);
                                                      changed = true;

                                                      return value;
                                                  },
                                                  (_, oldValue) =>
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
                foreach (var (key, value) in collectionNotifications)
                    key.OnCollectionChanged(value);
            }

            if (!PropertySessions.TryRemove(session, out var propertyNotifications))
                return;

            foreach (var (notifier, value) in propertyNotifications)
            {
                if (!notifier.IsNotificationsEnabled)
                    continue;

                foreach (var pd in value)
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

        void IgnoreCollectionChangesOn(string referenceName, INotifyCollectionChanged? ignored)
        {
            if (ignored is not null)
                ignored.CollectionChanged -= RespondToCollectionChanges;

            _collectionReferenceMap.Remove(referenceName);
        }

        void IgnoreCollectionItemPropertyReactionsOn(string referenceName, IReactToCollectionItemProperty? ignored)
        {
            if (ignored is not null)
                ignored.ReactToCollectionItemProperty -= RespondToCollectionItemPropertyReactions;

            _collectionItemsReferenceMap.Remove(referenceName);
        }

        void IgnoreCollectionReactionsOn(string referenceName, IReactToCollection? ignored)
        {
            if (ignored is not null)
                ignored.ReactToCollection -= RespondToCollectionReactions;

            _collectionReferenceMap.Remove(referenceName);
        }

        void IgnorePropertyChangesOn(string referenceName, INotifyPropertyChanged? ignored)
        {
            if (ignored is not null)
                ignored.PropertyChanged -= RespondToPropertyChanges;

            _propertyReferenceMap.Remove(referenceName);
        }

        void IgnorePropertyReactionsOn(string referenceName, Notifier? ignored)
        {
            if (ignored is not null)
                ignored._reactToProperty -= RespondToPropertyReactions;

            _propertyReferenceMap.Remove(referenceName);
        }

        void ListenForCollectionChangesOn(string referenceName, INotifyCollectionChanged? listened)
        {
            if (listened is null)
                return;

            listened.CollectionChanged += RespondToCollectionChanges;
            _collectionReferenceMap.Add(referenceName, listened);
        }

        void ListenForCollectionItemPropertyReactionsOn(string referencedName, IReactToCollectionItemProperty? listened)
        {
            if (listened is null)
                return;

            listened.ReactToCollectionItemProperty += RespondToCollectionItemPropertyReactions;
            _collectionItemsReferenceMap.Add(referencedName, listened);
        }

        void ListenForCollectionReactionsOn(string referenceName, IReactToCollection? listened)
        {
            if (listened is null)
                return;

            listened.ReactToCollection += RespondToCollectionReactions;
            _collectionReferenceMap.Add(referenceName, listened);
        }

        void ListenForPropertyChangesOn(string referenceName, INotifyPropertyChanged? listened)
        {
            if (listened is null)
                return;

            listened.PropertyChanged += RespondToPropertyChanges;
            _propertyReferenceMap.Add(referenceName, listened);
        }

        void ListenForPropertyReactionsOn(string referenceName, Notifier? listened)
        {
            if (listened is null)
                return;

            listened._reactToProperty += RespondToPropertyReactions;
            _propertyReferenceMap.Add(referenceName, listened);
        }

        void ListenTo<TProp>(TProp? value, string propertyName)
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

        void RespondToCollectionChanges(object? sender, NotifyCollectionChangedEventArgs args)
        {
            if (sender is not INotifyCollectionChanged)
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

        void RespondToPropertyChanges(object? sender, PropertyChangedEventArgs args)
        {
            if (sender is not INotifyPropertyChanged)
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

        void RespondToPropertyReactions(object? sender, ReactToPropertyEventArgs args)
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

            protected internal PropertyDependencyMapper(string dependentPropertyName, Notifier notifier) => (_notifier, _dependentPropertyName) = (notifier, dependentPropertyName);

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
#endif
