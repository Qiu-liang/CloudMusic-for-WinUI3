namespace music.Core.Abstractions
{
    public interface ISettingsStore
    {
        bool ContainsKey(string key);
        string? GetString(string key);
        long GetInt64(string key, long defaultValue = 0);
        bool GetBoolean(string key, bool defaultValue = false);
        void SetString(string key, string value);
        void SetInt64(string key, long value);
        void SetBoolean(string key, bool value);
        void Remove(string key);
        IDictionary<string, object> AsDictionary();
    }
}
