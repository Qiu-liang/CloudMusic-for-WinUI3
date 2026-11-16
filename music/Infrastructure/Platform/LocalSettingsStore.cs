using System;
using System.Collections.Generic;
using Windows.Storage;
using music.Core.Abstractions;

namespace music.Infrastructure.Platform
{
    public sealed class LocalSettingsStore : ISettingsStore
    {
        private readonly ApplicationDataContainer _container;

        public LocalSettingsStore() : this(ApplicationData.Current.LocalSettings) { }

        public LocalSettingsStore(ApplicationDataContainer container)
        {
            _container = container;
        }

        public bool ContainsKey(string key) => _container.Values.ContainsKey(key);

        public string? GetString(string key) =>
            _container.Values.TryGetValue(key, out var val) ? val?.ToString() : null;

        public long GetInt64(string key, long defaultValue = 0) =>
            _container.Values.TryGetValue(key, out var val) && val != null
                ? Convert.ToInt64(val)
                : defaultValue;

        public bool GetBoolean(string key, bool defaultValue = false) =>
            _container.Values.TryGetValue(key, out var val) && val is bool b ? b : defaultValue;

        public void SetString(string key, string value) => _container.Values[key] = value;

        public void SetInt64(string key, long value) => _container.Values[key] = value;

        public void SetBoolean(string key, bool value) => _container.Values[key] = value;

        public void Remove(string key) => _container.Values.Remove(key);

        public IDictionary<string, object> AsDictionary() => _container.Values;
    }
}
