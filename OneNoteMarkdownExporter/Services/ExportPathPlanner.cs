using System;
using System.Collections.Generic;
using System.IO;
using OneNoteMarkdownExporter.Models;

namespace OneNoteMarkdownExporter.Services
{
    public sealed class PageExportContext
    {
        public OneNoteItem Page { get; init; } = new();
        public string MarkdownFolderPath { get; init; } = string.Empty;
        public string MarkdownFilePath { get; init; } = string.Empty;
        public string AssetsFolderPath { get; init; } = string.Empty;
        public string RelativeAssetsPath { get; init; } = string.Empty;
    }

    public sealed class ExportPathPlanner
    {
        private readonly string _outputRoot;
        private readonly ExportOptions _options;
        private readonly Dictionary<OneNoteItem, int> _reservedDirectoryLengths = new();
        private readonly Dictionary<string, HashSet<string>> _claimedAssetNamesByParent = new(StringComparer.OrdinalIgnoreCase);

        public ExportPathPlanner(string outputRoot, ExportOptions options)
        {
            _outputRoot = string.IsNullOrWhiteSpace(outputRoot)
                ? ExportOptions.GetDefaultOutputPath()
                : Path.GetFullPath(outputRoot);
            _options = options;
        }

        public string OutputRoot => _outputRoot;

        public void ValidatePaths(IEnumerable<OneNoteItem> items)
        {
            _reservedDirectoryLengths.Clear();
            var hasExportItems = false;

            foreach (var item in items)
            {
                if (!item.IsSelected && !ExportSelectionHelper.HasSelectedDescendants(item))
                {
                    continue;
                }

                hasExportItems = true;
                var minimumDirectoryLength = GetMinimumItemDirectoryLength(item, hasScopedAssetsFolder: false);
                var minimumFileLength = 0;
                if (item.Type == OneNoteItemType.Page && item.IsSelected)
                {
                    var nameLength = _options.Overwrite
                        ? ExportPathSanitizer.GetMinimumComponentLength(item.Name)
                        : ExportPathSanitizer.HashLength;
                    minimumFileLength = 1 + nameLength + ".md".Length;
                }

                var rootLength = _outputRoot.Length - (Path.EndsInDirectorySeparator(_outputRoot) ? 1 : 0);
                if ((minimumDirectoryLength > 0 && rootLength + minimumDirectoryLength > ExportPathSanitizer.MaxWin32DirectoryPathLength)
                    || (minimumFileLength > 0 && rootLength + minimumFileLength > ExportPathSanitizer.MaxWin32PathLength))
                {
                    throw new PathTooLongException($"The export path is too long to preserve the notebook hierarchy under '{_outputRoot}', even with compact names. Choose a shorter output folder.");
                }
            }

            if (hasExportItems && _options.AssetOrganizationMode == AssetOrganizationMode.Centralized)
            {
                ExportPathSanitizer.GetSafeAssetFileName(GetCentralizedAssetsFolderPath(), null, 1, ".png");
            }
        }

        public string GetContainerFolderPath(string parentPath, OneNoteItem container)
        {
            var reservedLength = GetReservedDirectoryLength(container, hasScopedAssetsFolder: false);
            return ExportPathSanitizer.GetSafeDirectoryPath(parentPath, container.Name, container.Id, reservedPathLength: reservedLength);
        }

        public string GetChildPageFolderPath(string parentPath, OneNoteItem page)
        {
            var reservedLength = GetReservedDirectoryLength(page, hasScopedAssetsFolder: false);
            return ExportPathSanitizer.GetSafeDirectoryPath(parentPath, page.Name, page.Id, reservedPathLength: reservedLength);
        }

        public string GetCentralizedAssetsFolderPath()
        {
            return AssetPathResolver.ResolveAssetsFolderPath(_outputRoot, _options.AssetsFolderPath);
        }

        public string GetScopedAssetsFolderPath(string parentPath, OneNoteItem scope)
        {
            var claimedNames = GetClaimedNames(parentPath);
            return ExportPathSanitizer.GetSafeAssetScopeDirectoryPath(parentPath, AssetPathResolver.DefaultAssetsFolderName, scope.Name, scope.Id, claimedNames);
        }

        public PageExportContext CreatePageContext(OneNoteItem page, string markdownFolderPath, string assetsFolderPath)
        {
            return new PageExportContext
            {
                Page = page,
                MarkdownFolderPath = markdownFolderPath,
                MarkdownFilePath = ExportPathSanitizer.GetSafeMarkdownFilePath(markdownFolderPath, page.Name, page.Id),
                AssetsFolderPath = assetsFolderPath,
                RelativeAssetsPath = AssetPathResolver.GetRelativeAssetsPath(markdownFolderPath, assetsFolderPath)
            };
        }

        private int GetMinimumItemDirectoryLength(OneNoteItem item, bool hasScopedAssetsFolder)
        {
            if (item.Type != OneNoteItemType.Page)
            {
                return 1 + ExportPathSanitizer.GetMinimumComponentLength(item.Name)
                    + GetReservedDirectoryLength(item, hasScopedAssetsFolder);
            }

            var minimumLength = PageNeedsScopedAssetsFolder(hasScopedAssetsFolder)
                ? GetMinimumAssetsDirectoryLength(item)
                : 0;

            if (item.Children.Count > 0)
            {
                minimumLength = Math.Max(minimumLength,
                    1 + ExportPathSanitizer.GetMinimumComponentLength(item.Name)
                    + GetReservedDirectoryLength(item, hasScopedAssetsFolder));
            }

            return minimumLength;
        }

        private int GetReservedDirectoryLength(OneNoteItem item, bool hasScopedAssetsFolder)
        {
            if (_reservedDirectoryLengths.TryGetValue(item, out var reservedLength))
            {
                return reservedLength;
            }

            var ownsAssetsFolder = (_options.AssetOrganizationMode == AssetOrganizationMode.Notebook && item.Type == OneNoteItemType.Notebook)
                || (_options.AssetOrganizationMode == AssetOrganizationMode.Section && item.Type == OneNoteItemType.Section);
            reservedLength = ownsAssetsFolder ? GetMinimumAssetsDirectoryLength(item) : 0;
            hasScopedAssetsFolder |= ownsAssetsFolder;

            // Selection must not change the shortened paths used by full and partial exports.
            foreach (var child in item.Children)
            {
                reservedLength = Math.Max(reservedLength, GetMinimumItemDirectoryLength(child, hasScopedAssetsFolder));
            }

            _reservedDirectoryLengths[item] = reservedLength;
            return reservedLength;
        }

        private bool PageNeedsScopedAssetsFolder(bool hasScopedAssetsFolder)
        {
            return _options.AssetOrganizationMode == AssetOrganizationMode.Page
                || (!hasScopedAssetsFolder && (_options.AssetOrganizationMode == AssetOrganizationMode.Notebook
                    || _options.AssetOrganizationMode == AssetOrganizationMode.Section));
        }

        private static int GetMinimumAssetsDirectoryLength(OneNoteItem scope)
        {
            var name = ExportPathSanitizer.GetAssetScopeFolderName(AssetPathResolver.DefaultAssetsFolderName, scope.Name);
            return 1 + ExportPathSanitizer.GetMinimumComponentLength(name);
        }

        private HashSet<string> GetClaimedNames(string parentPath)
        {
            var fullParentPath = Path.GetFullPath(parentPath);
            if (!_claimedAssetNamesByParent.TryGetValue(fullParentPath, out var claimedNames))
            {
                claimedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _claimedAssetNamesByParent[fullParentPath] = claimedNames;
            }

            return claimedNames;
        }
    }
}
