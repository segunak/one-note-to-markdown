using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace OneNoteMarkdownExporter.Services
{
    public static class ExportPathSanitizer
    {
        public const int MaxWin32PathLength = 259;
        public const int MaxWin32DirectoryPathLength = MaxWin32PathLength - 14;

        private const int MaxComponentLength = 255;
        internal const int HashLength = 8;
        private const string DefaultFallbackName = "Untitled";

        private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "COM\u00b9", "COM\u00b2", "COM\u00b3",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
            "LPT\u00b9", "LPT\u00b2", "LPT\u00b3"
        };

        public static string SanitizeComponent(string? name, string fallbackName = DefaultFallbackName, string? stableId = null)
        {
            var sanitized = SanitizeRawComponent(name);
            if (IsEmptySanitizedName(sanitized))
            {
                sanitized = SanitizeRawComponent(fallbackName);
            }

            if (IsEmptySanitizedName(sanitized))
            {
                sanitized = DefaultFallbackName;
            }

            return ProtectReservedName(sanitized);
        }

        public static string GetMarkdownFileName(string? pageName, string? pageId = null)
        {
            return $"{SanitizeComponent(pageName, DefaultFallbackName, pageId)}.md";
        }

        internal static int GetMinimumComponentLength(string? name)
        {
            return Math.Min(SanitizeComponent(name).Length, HashLength);
        }

        public static string GetSafeDirectoryPath(string parentPath, string? componentName, string? stableId = null, string fallbackName = DefaultFallbackName, int reservedPathLength = 0)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(reservedPathLength);

            var component = SanitizeComponent(componentName, fallbackName, stableId);
            var safeComponent = FitComponentToPath(parentPath, component, string.Empty, stableId ?? componentName ?? fallbackName, MaxWin32DirectoryPathLength - reservedPathLength);

            return Path.Combine(parentPath, safeComponent);
        }

        public static string GetSafeMarkdownFilePath(string folderPath, string? pageName, string? pageId = null, int copyCounter = 0)
        {
            var stem = SanitizeComponent(pageName, DefaultFallbackName, pageId);
            var stableInput = pageId ?? pageName ?? DefaultFallbackName;
            if (copyCounter > 0)
            {
                stem = $"{stem} ({copyCounter})";
                stableInput = FormattableString.Invariant($"{stableInput}\0copy:{copyCounter}");
            }

            var safeFileName = FitComponentToPath(folderPath, stem, ".md", stableInput, MaxWin32PathLength);

            return Path.Combine(folderPath, safeFileName);
        }

        public static string GetSafeAssetFileName(string assetsFolder, string? pagePrefix, int imageIndex, string extension)
        {
            var normalizedExtension = NormalizeExtension(extension);
            var imageStem = $"image_{imageIndex:D4}";
            var stem = string.IsNullOrWhiteSpace(pagePrefix)
                ? imageStem
                : $"{SanitizeComponent(pagePrefix, "page", pagePrefix)}_{imageStem}";

            var stableInput = FormattableString.Invariant($"{pagePrefix ?? imageStem}\0image:{imageIndex}");
            return FitComponentToPath(assetsFolder, stem, normalizedExtension, stableInput, MaxWin32PathLength);
        }

        public static string GetSafeAssetScopeDirectoryPath(
            string parentPath,
            string prefix,
            string? scopeName,
            string? stableId = null,
            ISet<string>? claimedNames = null)
        {
            var baseName = GetAssetScopeFolderName(prefix, scopeName);
            var stableInput = stableId ?? scopeName ?? baseName;
            var folderName = FitComponentToPath(parentPath, baseName, string.Empty, stableInput, MaxWin32DirectoryPathLength);

            if (claimedNames == null || claimedNames.Add(folderName))
            {
                return Path.Combine(parentPath, folderName);
            }

            var hash = GetStableHash(stableInput);
            var uniqueStem = $"{baseName}_{hash}";
            folderName = FitComponentToPath(parentPath, uniqueStem, string.Empty, $"{stableInput}\0collision:1", MaxWin32DirectoryPathLength);

            var counter = 2;
            while (!claimedNames.Add(folderName))
            {
                var collisionInput = FormattableString.Invariant($"{stableInput}\0collision:{counter}");
                folderName = FitComponentToPath(parentPath, $"{uniqueStem}_{counter}", string.Empty, collisionInput, MaxWin32DirectoryPathLength);
                counter++;
            }

            return Path.Combine(parentPath, folderName);
        }

        public static string GetAssetScopeFolderName(string prefix, string? scopeName)
        {
            var safePrefix = SanitizeComponent(prefix, "assets").Replace(" ", string.Empty);
            var suffix = ToPascalCaseScopeName(scopeName);

            return $"{safePrefix}_{suffix}";
        }

        private static string SanitizeRawComponent(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var sanitized = new StringBuilder(name.Length);
            foreach (var character in name)
            {
                sanitized.Append(GetSafeReplacementCharacter(character));
            }

            var result = Regex.Replace(sanitized.ToString(), "_{2,}", "_").Trim();
            result = TrimTrailingSpacesAndPeriods(result);

            return result;
        }

        private static char GetSafeReplacementCharacter(char character)
        {
            if (character == '/' || character == '\\')
            {
                return '-';
            }

            return IsInvalidWindowsFileNameCharacter(character) ? '_' : character;
        }

        private static string ToPascalCaseScopeName(string? name)
        {
            var sanitized = SanitizeRawComponent(name);
            var parts = new List<string>();
            var current = new StringBuilder();

            foreach (var character in sanitized)
            {
                if (char.IsLetterOrDigit(character))
                {
                    current.Append(character);
                }
                else if (IsWordJoinerPunctuation(character))
                {
                    continue;
                }
                else if (current.Length > 0)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }
            }

            if (current.Length > 0)
            {
                parts.Add(current.ToString());
            }

            if (parts.Count == 0)
            {
                return "Assets";
            }

            var result = new StringBuilder();
            foreach (var part in parts)
            {
                result.Append(char.ToUpperInvariant(part[0]));
                if (part.Length > 1)
                {
                    result.Append(part[1..]);
                }
            }

            return ProtectReservedName(result.ToString());
        }

        private static bool IsWordJoinerPunctuation(char character)
        {
            return character == '\''
                || character == '\u2019'
                || character == '\u2018'
                || character == '\u02bc'
                || character == '\uff07';
        }

        private static string TrimTrailingSpacesAndPeriods(string value)
        {
            var length = value.Length;
            while (length > 0 && (char.IsWhiteSpace(value[length - 1]) || value[length - 1] == '.'))
            {
                length--;
            }

            return value[..length];
        }

        private static bool IsInvalidWindowsFileNameCharacter(char character)
        {
            return character == '<'
                || character == '>'
                || character == ':'
                || character == '"'
                || character == '/'
                || character == '\\'
                || character == '|'
                || character == '?'
                || character == '*'
                || character <= 31;
        }

        private static bool IsEmptySanitizedName(string name)
        {
            return string.IsNullOrWhiteSpace(name) || name.All(character => character == '_' || character == '-' || char.IsWhiteSpace(character));
        }

        private static string ProtectReservedName(string component)
        {
            var firstPeriodIndex = component.IndexOf('.');
            var stem = firstPeriodIndex >= 0 ? component[..firstPeriodIndex] : component;

            if (!ReservedNames.Contains(stem))
            {
                return component;
            }

            return firstPeriodIndex >= 0
                ? $"{stem}_{component[firstPeriodIndex..]}"
                : $"{component}_";
        }

        private static string FitComponentToPath(string parentPath, string stem, string extension, string stableInput, int maxPathLength)
        {
            extension = NormalizeExtension(extension);
            var availableLength = GetAvailableComponentLength(parentPath, maxPathLength);
            var maxComponentLength = Math.Min(MaxComponentLength, availableLength);

            if (maxComponentLength < extension.Length + Math.Min(stem.Length, HashLength))
            {
                throw new PathTooLongException($"The export path is too long to create a file or folder under '{parentPath}' while preserving a safe name. Choose a shorter output or assets folder.");
            }

            var fullComponent = $"{stem}{extension}";
            if (fullComponent.Length <= maxComponentLength)
            {
                return fullComponent;
            }

            return ShortenComponent(stem, extension, maxComponentLength, stableInput);
        }

        private static int GetAvailableComponentLength(string parentPath, int maxPathLength)
        {
            var fullParentPath = Path.GetFullPath(parentPath);
            var separatorLength = fullParentPath.EndsWith(Path.DirectorySeparatorChar) || fullParentPath.EndsWith(Path.AltDirectorySeparatorChar)
                ? 0
                : 1;

            return maxPathLength - fullParentPath.Length - separatorLength;
        }

        private static string ShortenComponent(string stem, string extension, int maxComponentLength, string stableInput)
        {
            var maxStemLength = maxComponentLength - extension.Length;
            if (maxStemLength <= 0)
            {
                throw new PathTooLongException("The export path is too long to preserve the file extension.");
            }

            var hash = GetStableHash(stableInput);
            var suffix = $"_{hash}";
            if (maxStemLength <= suffix.Length)
            {
                return $"{hash}{extension}";
            }

            var prefixLength = maxStemLength - suffix.Length;
            var prefix = stem.Length <= prefixLength ? stem : stem[..prefixLength];
            prefix = prefix.TrimEnd(' ', '.', '_');

            if (string.IsNullOrWhiteSpace(prefix))
            {
                prefix = DefaultFallbackName[..Math.Min(DefaultFallbackName.Length, prefixLength)];
            }

            return $"{prefix}{suffix}{extension}";
        }

        private static string GetStableHash(string stableInput)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(stableInput));
            return string.Concat(bytes.Take(HashLength / 2).Select(value => value.ToString("x2")));
        }

        private static string NormalizeExtension(string extension)
        {
            if (string.IsNullOrEmpty(extension))
            {
                return string.Empty;
            }

            return extension.StartsWith('.') ? extension : $".{extension}";
        }
    }
}
