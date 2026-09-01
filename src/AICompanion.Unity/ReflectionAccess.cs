using System;
using System.Reflection;

namespace AICompanion.Unity
{
    internal static class ReflectionAccess
    {
        private const BindingFlags AllInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static T? Field<T>(object? instance, string name) where T : class
        {
            if (instance == null) return null;
            return instance.GetType().GetField(name, AllInstance)?.GetValue(instance) as T;
        }

        public static object? Field(object? instance, string name)
        {
            if (instance == null) return null;
            return instance.GetType().GetField(name, AllInstance)?.GetValue(instance);
        }

        public static bool BoolField(object? instance, string name)
        {
            var value = Field(instance, name);
            return value is bool result && result;
        }

        public static object? Property(object? instance, string name)
        {
            if (instance == null) return null;
            return instance.GetType().GetProperty(name, AllInstance)?.GetValue(instance, null);
        }
    }
}
