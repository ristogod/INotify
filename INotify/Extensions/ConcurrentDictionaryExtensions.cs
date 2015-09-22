using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace INotify.Extensions
{
    internal static class ConcurrentDictionaryExtensions
    {
        public static IEnumerable<TValue> GetValues<TKey, TValue>(this ConcurrentDictionary<TKey, TValue> dictionary)
        {
            lock (dictionary)
            {
                return from pair in dictionary select pair.Value;
            }
        }
    }
}
