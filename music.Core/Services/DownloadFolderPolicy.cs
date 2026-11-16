using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace music.Core.Services
{
    /// <summary>
    /// Centralizes resolution, validation, and settings dictionary persistence of the music download directory.
    /// Defaults to a "CloudMusic for WinUI" subfolder inside the Windows user's Music folder.
    /// </summary>
    public static class DownloadFolderPolicy
    {
        public const string SettingsKey = "DownloadFolder";
        public const string DefaultSubfolderName = "CloudMusic for WinUI";

        /// <summary>
        /// Computes the default download folder under the user's Windows Music directory.
        /// Accepts optional overrides so unit tests can run deterministically without OS state.
        /// </summary>
        public static string GetDefaultDownloadFolder(
            string? myMusicFolder = null,
            string? userProfileFolder = null,
            string? localAppFolder = null)
        {
            var musicRoot = myMusicFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            if (!string.IsNullOrWhiteSpace(musicRoot))
            {
                return NormalizeDirectoryPath(Path.Combine(musicRoot.Trim(), DefaultSubfolderName));
            }

            var profileRoot = userProfileFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(profileRoot))
            {
                return NormalizeDirectoryPath(Path.Combine(profileRoot.Trim(), "Music", DefaultSubfolderName));
            }

            var localBase = string.IsNullOrWhiteSpace(localAppFolder)
                ? AppContext.BaseDirectory
                : localAppFolder;

            return NormalizeDirectoryPath(Path.Combine(localBase.Trim(), "Downloads"));
        }

        /// <summary>
        /// Normalizes a directory path by trimming whitespace and trailing separators (except drive roots).
        /// </summary>
        public static string NormalizeDirectoryPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;

            var trimmed = path.Trim();
            var root = Path.GetPathRoot(trimmed);
            if (!string.IsNullOrEmpty(root) && trimmed.Length > root.Length)
            {
                return trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }

            return trimmed;
        }

        /// <summary>
        /// Resolves a configured download path, falling back to <paramref name="defaultFolder"/>
        /// when empty, relative, or invalid.
        /// </summary>
        public static string ResolveDownloadFolder(string? configuredPath, string defaultFolder)
        {
            var normalizedDefault = NormalizeDirectoryPath(defaultFolder);
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                return normalizedDefault;
            }

            var candidate = configuredPath.Trim();
            var invalidChars = Path.GetInvalidPathChars();
            if (candidate.Any(c => invalidChars.Contains(c)))
            {
                return normalizedDefault;
            }

            if (!Path.IsPathFullyQualified(candidate))
            {
                return normalizedDefault;
            }

            return NormalizeDirectoryPath(candidate);
        }

        /// <summary>
        /// Returns true if the resolved path points to <paramref name="defaultFolder"/>.
        /// </summary>
        public static bool IsDefaultDownloadFolder(string? configuredPath, string defaultFolder)
        {
            var resolved = ResolveDownloadFolder(configuredPath, defaultFolder);
            var normalizedDefault = NormalizeDirectoryPath(defaultFolder);
            return string.Equals(resolved, normalizedDefault, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Reads the effective download folder from a settings dictionary.
        /// </summary>
        public static string GetCurrentDownloadFolder(
            IDictionary<string, object> settingsValues,
            string? localAppFolder = null)
        {
            var defaultFolder = GetDefaultDownloadFolder(localAppFolder: localAppFolder);
            var saved = settingsValues.TryGetValue(SettingsKey, out var value) ? value?.ToString() : null;
            return ResolveDownloadFolder(saved, defaultFolder);
        }

        /// <summary>
        /// Persists a custom download folder in a settings dictionary, or removes the key when matching default.
        /// </summary>
        public static string SaveDownloadFolder(
            IDictionary<string, object> settingsValues,
            string? folderPath,
            string? localAppFolder = null)
        {
            var defaultFolder = GetDefaultDownloadFolder(localAppFolder: localAppFolder);
            var resolved = ResolveDownloadFolder(folderPath, defaultFolder);

            if (IsDefaultDownloadFolder(resolved, defaultFolder))
            {
                settingsValues.Remove(SettingsKey);
            }
            else
            {
                settingsValues[SettingsKey] = resolved;
            }

            return resolved;
        }

        /// <summary>
        /// Clears any custom download folder setting and returns the default folder path.
        /// </summary>
        public static string ResetDownloadFolder(
            IDictionary<string, object> settingsValues,
            string? localAppFolder = null)
        {
            settingsValues.Remove(SettingsKey);
            return GetDefaultDownloadFolder(localAppFolder: localAppFolder);
        }
    }
}
