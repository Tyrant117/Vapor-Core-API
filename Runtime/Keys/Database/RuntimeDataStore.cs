using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
using Vapor.Inspector;

namespace Vapor.Keys
{
    public class RuntimeDataStore<T>
    {
        private static Dictionary<int, T> s_Db;
        public static T Get(int id) => s_Db[id];
        public static U Get<U>(int id) where U : T => (U)s_Db[id];
        public static bool TryGet(int id, out T value) => s_Db.TryGetValue(id, out value);
        public static IEnumerable<T> All() => s_Db.Values;
        public static int Count => s_Db.Count;

        public static void InitDatabase(int capacity)
        {
            if (DatabaseBootstrapper.HasInitDataStore(typeof(T)))
            {
                return;
            }

            s_Db ??= new Dictionary<int, T>();
            s_Db.Clear();
            s_Db.EnsureCapacity(capacity);
            DatabaseBootstrapper.InitDataStore(typeof(T));
            Debug.Log($"{TooltipMarkup.ClassMethod(nameof(RuntimeDataStore<T>), nameof(InitDatabase))} - {capacity}");
        }

        public static void Add(int key, T value)
        {
            s_Db[key] = value;
        }
    }
}
