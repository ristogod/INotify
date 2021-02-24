using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace INotify.Core.Extensions
{
    static class DictionaryExtensions
    {
        #region methods

        public static IEnumerable<TKey> FindAllKeys<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TValue value) where TKey : notnull =>
            dictionary.Where(pair => pair.Value?.Equals(value) ?? value is null)
                      .Select(pair => pair.Key);

        public static TKey FindKey<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TValue value) where TKey : notnull =>
            dictionary.SingleOrDefault(pair => pair.Value?.Equals(value) ?? value is null)
                      .Key;

        public static IEnumerable<TValue> GetValues<TKey, TValue>(this Dictionary<TKey, TValue> dictionary) where TKey : notnull => dictionary.Select(pair => pair.Value);

        public static IEnumerable<TValue> GetValues<TKey, TValue>(this ConcurrentDictionary<TKey, TValue> dictionary) where TKey : notnull
        {
            lock (dictionary)
                return dictionary.Select(pair => pair.Value);
        }

        #endregion
    }
}
