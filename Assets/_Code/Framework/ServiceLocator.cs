using System;
using System.Collections.Generic;

namespace Framework
{
    public static class ServiceLocator
    {
        private static readonly Dictionary<Type, object> Services = new();
        public static void Register<T>(T instance) where T : class => Services[typeof(T)] = instance;
        public static T Get<T>() where T : class => Services.TryGetValue(typeof(T), out var s) ? s as T : null;
    }
}