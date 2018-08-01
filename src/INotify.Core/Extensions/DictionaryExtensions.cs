using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace INotify.Core.Extensions
{
    static class DictionaryExtensions
    {
        #region methods

        public static IEnumerable<TKey> FindAllKeys<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TValue value) =>
            dictionary.Where(pair => pair.Value.Equals(value))
                      .Select(pair => pair.Key);

        public static TKey FindKey<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TValue value) =>
            dictionary.SingleOrDefault(pair => pair.Value.Equals(value))
                      .Key;

        public static IEnumerable<TValue> GetValues<TKey, TValue>(this Dictionary<TKey, TValue> dictionary) => dictionary.Select(pair => pair.Value);

        public static IEnumerable<TValue> GetValues<TKey, TValue>(this ConcurrentDictionary<TKey, TValue> dictionary)
        {
            lock (dictionary)
                return dictionary.Select(pair => pair.Value);
        }

        #endregion
    }
}
