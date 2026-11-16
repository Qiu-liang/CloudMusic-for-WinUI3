using System;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Security.Credentials;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;
using Windows.Storage;
using music.Core.Abstractions;

namespace music.Infrastructure.Platform
{
    public sealed class WindowsCredentialStore : ICredentialStore
    {
        private const string Resource = "CloudMusic.ApiSession";
        private const string UserName = "SessionCookie";
        private const string ProtectedCookieKey = "ProtectedCookie";

        private readonly ApplicationDataContainer _settings;

        public WindowsCredentialStore() : this(ApplicationData.Current.LocalSettings)
        {
        }

        public WindowsCredentialStore(ApplicationDataContainer settings)
        {
            _settings = settings;
        }

        public string Load()
        {
            try
            {
                var credential = new PasswordVault().Retrieve(Resource, UserName);
                credential.RetrievePassword();
                if (!string.IsNullOrEmpty(credential.Password))
                {
                    return credential.Password;
                }
            }
            catch { }

            if (_settings.Values[ProtectedCookieKey] is string protectedValue)
            {
                try
                {
                    return RunProtectedAsync(async () =>
                    {
                        var buffer = await new DataProtectionProvider().UnprotectAsync(
                            CryptographicBuffer.DecodeFromBase64String(protectedValue));
                        return Encoding.UTF8.GetString(buffer.ToArray());
                    });
                }
                catch { }
            }

            var legacyCookie = _settings.Values["Cookie"]?.ToString() ?? string.Empty;
            if (!string.IsNullOrEmpty(legacyCookie))
            {
                Save(legacyCookie);
            }
            RemovePlaintextCookies(_settings);
            return legacyCookie;
        }

        public void Save(string cookie)
        {
            RemovePlaintextCookies(_settings);
            if (string.IsNullOrEmpty(cookie)) return;

            try
            {
                ClearVault();
                new PasswordVault().Add(new PasswordCredential(Resource, UserName, cookie));
                _settings.Values.Remove(ProtectedCookieKey);
                return;
            }
            catch { }

            try
            {
                _settings.Values[ProtectedCookieKey] = RunProtectedAsync(async () =>
                {
                    var provider = new DataProtectionProvider("LOCAL=user");
                    var buffer = await provider.ProtectAsync(Encoding.UTF8.GetBytes(cookie).AsBuffer());
                    return CryptographicBuffer.EncodeToBase64String(buffer);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Security] Unable to protect session cookie: {ex.Message}");
                _settings.Values.Remove(ProtectedCookieKey);
            }
        }

        public void Clear()
        {
            ClearVault();
            RemovePlaintextCookies(_settings);
            _settings.Values.Remove(ProtectedCookieKey);
        }

        private static T RunProtectedAsync<T>(Func<Task<T>> action) =>
            Task.Run(action).GetAwaiter().GetResult();

        private static void ClearVault()
        {
            try
            {
                var vault = new PasswordVault();
                var credentials = vault.FindAllByResource(Resource);
                foreach (var credential in credentials)
                {
                    vault.Remove(credential);
                }
            }
            catch { }
        }

        private static void RemovePlaintextCookies(ApplicationDataContainer settings)
        {
            settings.Values.Remove("Cookie");
            foreach (var key in settings.Values.Keys.Where(key => key.StartsWith("Cookie_", StringComparison.Ordinal)).ToList())
            {
                settings.Values.Remove(key);
            }
        }
    }
}
