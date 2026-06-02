using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OneNoteMarkdownExporter.Models;

namespace OneNoteMarkdownExporter.Services
{
    /// <summary>
    /// Service for exporting OneNote content to Markdown.
    /// This service is UI-independent and can be used by both GUI and CLI.
    /// </summary>
    public class ExportService
    {
        private readonly OneNoteService _oneNoteService;
        private readonly OneNoteXmlToMarkdownConverter _xmlConverter;
        private readonly MarkdownLintCliService _cliLinter;

        public ExportService()
        {
            _oneNoteService = new OneNoteService();
            _xmlConverter = new OneNoteXmlToMarkdownConverter();
            _cliLinter = new MarkdownLintCliService();
        }

        /// <summary>
        /// Gets the notebook hierarchy from OneNote.
        /// </summary>
        public List<OneNoteItem> GetNotebookHierarchy()
        {
            return _oneNoteService.GetNotebookHierarchy();
        }

        /// <summary>
        /// Checks if markdownlint-cli is available.
        /// </summary>
        public bool IsMarkdownCliLinterAvailable => _cliLinter.IsAvailable;

        /// <summary>
        /// Gets the reason why markdownlint-cli is unavailable.
        /// </summary>
        public string MarkdownCliLinterUnavailableReason => _cliLinter.UnavailableReason;

        /// <summary>
        /// Exports OneNote content to Markdown files.
        /// </summary>
        /// <param name="options">Export options including output path and selection criteria.</param>
        /// <param name="progress">Optional progress reporter for logging.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Export result with statistics.</returns>
        public async Task<ExportResult> ExportAsync(
            ExportOptions options,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new ExportResult();
            
            try
            {
                // Validate options
                if (string.IsNullOrWhiteSpace(options.OutputPath))
                {
                    options.OutputPath = ExportOptions.GetDefaultOutputPath();
                }

                // Ensure output directory exists
                if (!Directory.Exists(options.OutputPath))
                {
                    Directory.CreateDirectory(options.OutputPath);
                }

                // Get notebook hierarchy
                progress?.Report("Loading OneNote hierarchy...");
                var notebooks = _oneNoteService.GetNotebookHierarchy();

                // Apply selection criteria
                var selectedItems = ApplySelectionCriteria(notebooks, options);
                
                if (!selectedItems.Any())
                {
                    progress?.Report("No items match the selection criteria.");
                    return result;
                }

                result.TotalItems = ExportSelectionHelper.CountItemsToExport(selectedItems);
                progress?.Report($"Found {result.TotalItems} item(s) to export.");

                if (options.DryRun)
                {
                    progress?.Report("Dry run mode - listing items that would be exported:");
                    ListItems(selectedItems, progress, "");
                    return result;
                }

                var assetsRoot = AssetPathResolver.PrepareAssetsFolder(options.OutputPath, options.AssetsFolderPath);

                // Export items
                await Task.Run(() =>
                {
                    foreach (var item in selectedItems)
                    {
                        if (cancellationToken.IsCancellationRequested) break;
                        ExportItem(item, options.OutputPath, options.OutputPath, assetsRoot, options, result, progress, cancellationToken);
                    }
                }, cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                {
                    progress?.Report("Export cancelled by user.");
                }
                else
                {
                    progress?.Report($"Export completed. {result.ExportedPages} page(s) exported, {result.FailedPages} failed.");
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                progress?.Report($"Export failed: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Exports a single page synchronously. Useful for testing or simple scenarios.
        /// </summary>
        public string ExportPageToString(string pageId, ExportOptions options)
        {
            var pageXml = _oneNoteService.GetPageContent(pageId);
            
            // Create a binary content fetcher for images that aren't embedded
            BinaryContentFetcher binaryFetcher = (callbackId) => _oneNoteService.GetBinaryPageContent(pageId, callbackId);
            
            // Use a shortened hash of the pageId as prefix to avoid collisions (pageId is a GUID-like string)
            var pagePrefix = pageId.Length > 8 ? pageId.Substring(0, 8) : pageId;
            var outputPath = string.IsNullOrWhiteSpace(options.OutputPath)
                ? ExportOptions.GetDefaultOutputPath()
                : options.OutputPath;
            var assetsRoot = AssetPathResolver.ResolveAssetsFolderPath(outputPath, options.AssetsFolderPath);
            var relativeAssetsPath = AssetPathResolver.GetRelativeAssetsPath(outputPath, assetsRoot);
            var markdown = _xmlConverter.Convert(pageXml, assetsRoot, relativeAssetsPath, binaryFetcher, pagePrefix);

            if (options.ApplyLinting)
            {
                try
                {
                    markdown = _cliLinter.LintContent(markdown);
                }
                catch
                {
                    // Linting failed, continue with unlinted content
                }
            }

            return markdown;
        }

        private List<OneNoteItem> ApplySelectionCriteria(List<OneNoteItem> notebooks, ExportOptions options)
        {
            if (options.ExportAll)
            {
                // Select all items
                SelectAllRecursive(notebooks);
                return notebooks;
            }

            var result = new List<OneNoteItem>();

            // Filter by notebook names
            if (options.NotebookNames != null && options.NotebookNames.Count > 0)
            {
                foreach (var notebook in notebooks)
                {
                    if (options.NotebookNames.Any(n => 
                        notebook.Name.Equals(n, StringComparison.OrdinalIgnoreCase)))
                    {
                        SelectAllRecursive(notebook);
                        result.Add(notebook);
                    }
                }
            }

            // Filter by section paths
            if (options.SectionPaths != null && options.SectionPaths.Count > 0)
            {
                foreach (var sectionPath in options.SectionPaths)
                {
                    var item = FindItemByPath(notebooks, sectionPath);
                    if (item != null)
                    {
                        SelectAllRecursive(item);
                        // Add to result, ensuring parent structure is maintained
                        AddItemWithParentStructure(notebooks, item, result);
                    }
                }
            }

            // Filter by page IDs
            if (options.PageIds != null && options.PageIds.Count > 0)
            {
                foreach (var pageId in options.PageIds)
                {
                    var page = FindItemById(notebooks, pageId);
                    if (page != null)
                    {
                        page.IsSelected = true;
                        AddItemWithParentStructure(notebooks, page, result);
                    }
                }
            }

            return result.Count > 0 ? result : notebooks.Where(ExportSelectionHelper.HasSelectedDescendants).ToList();
        }

        private void SelectAllRecursive(List<OneNoteItem> items)
        {
            foreach (var item in items)
            {
                SelectAllRecursive(item);
            }
        }

        private void SelectAllRecursive(OneNoteItem item)
        {
            item.IsSelected = true;
            foreach (var child in item.Children)
            {
                SelectAllRecursive(child);
            }
        }

        private OneNoteItem? FindItemByPath(List<OneNoteItem> items, string path)
        {
            var parts = path.Split('/', '\\');
            var current = items;
            OneNoteItem? found = null;

            foreach (var part in parts)
            {
                found = current.FirstOrDefault(i => 
                    i.Name.Equals(part, StringComparison.OrdinalIgnoreCase));
                if (found == null) return null;
                current = found.Children;
            }

            return found;
        }

        private OneNoteItem? FindItemById(List<OneNoteItem> items, string id)
        {
            foreach (var item in items)
            {
                if (item.Id == id) return item;
                var found = FindItemById(item.Children, id);
                if (found != null) return found;
            }
            return null;
        }

        private void AddItemWithParentStructure(List<OneNoteItem> source, OneNoteItem target, List<OneNoteItem> result)
        {
            // For simplicity, just add the target if not already in result
            // In a real scenario, you might want to maintain parent hierarchy
            foreach (var item in source)
            {
                if (item == target || ContainsItem(item, target))
                {
                    if (!result.Contains(item))
                    {
                        result.Add(item);
                    }
                    return;
                }
            }
        }

        private bool ContainsItem(OneNoteItem parent, OneNoteItem target)
        {
            if (parent.Children.Contains(target)) return true;
            foreach (var child in parent.Children)
            {
                if (ContainsItem(child, target)) return true;
            }
            return false;
        }

        private void ListItems(List<OneNoteItem> items, IProgress<string>? progress, string indent, bool isImplicitlySelected = false)
        {
            foreach (var item in items)
            {
                var isSelected = item.IsSelected || isImplicitlySelected;
                if (isSelected || ExportSelectionHelper.HasSelectedDescendants(item))
                {
                    var typeStr = item.Type switch
                    {
                        OneNoteItemType.Notebook => "[Notebook]",
                        OneNoteItemType.SectionGroup => "[SectionGroup]",
                        OneNoteItemType.Section => "[Section]",
                        OneNoteItemType.Page => "[Page]",
                        _ => "[Unknown]"
                    };
                    progress?.Report($"{indent}{typeStr} {item.Name}");
                    ListItems(item.Children, progress, indent + "  ", isSelected);
                }
            }
        }

        private void ExportItem(
            OneNoteItem item,
            string currentPath,
            string rootPath,
            string assetsRoot,
            ExportOptions options,
            ExportResult result,
            IProgress<string>? progress,
            CancellationToken token,
            bool isImplicitlySelected = false)
        {
            if (token.IsCancellationRequested) return;

            bool isSelected = item.IsSelected || isImplicitlySelected;
            bool hasSelectedDescendants = ExportSelectionHelper.HasSelectedDescendants(item);

            if (!isSelected && !hasSelectedDescendants) return;

            string myPath = currentPath;

            if (item.Type != OneNoteItemType.Page)
            {
                // It's a container
                myPath = ExportPathSanitizer.GetSafeDirectoryPath(currentPath, item.Name, item.Id);
                if (!Directory.Exists(myPath))
                {
                    Directory.CreateDirectory(myPath);
                }

                foreach (var child in item.Children)
                {
                    if (token.IsCancellationRequested) return;

                    ExportItem(child, myPath, rootPath, assetsRoot, options, result, progress, token, isSelected);
                }
            }
            else
            {
                // It's a page
                if (isSelected)
                {
                    ExportPage(item, currentPath, rootPath, assetsRoot, options, result, progress, token);
                }

                if (item.Children.Count > 0)
                {
                    myPath = ExportPathSanitizer.GetSafeDirectoryPath(currentPath, item.Name, item.Id);
                    if (!Directory.Exists(myPath))
                    {
                        Directory.CreateDirectory(myPath);
                    }

                    foreach (var child in item.Children)
                    {
                        if (token.IsCancellationRequested) return;

                        ExportItem(child, myPath, rootPath, assetsRoot, options, result, progress, token, isSelected);
                    }
                }
            }
        }

        private void ExportPage(
            OneNoteItem page,
            string folderPath,
            string rootPath,
            string assetsRoot,
            ExportOptions options,
            ExportResult result,
            IProgress<string>? progress,
            CancellationToken token)
        {
            if (token.IsCancellationRequested) return;

            if (!options.Quiet)
            {
                progress?.Report($"Exporting: {page.Name}");
            }

            Directory.CreateDirectory(folderPath);
            var finalMdPath = ExportPathSanitizer.GetSafeMarkdownFilePath(folderPath, page.Name, page.Id);

            // Handle file existence based on overwrite setting
            if (File.Exists(finalMdPath))
            {
                if (options.Overwrite)
                {
                    if (options.Verbose)
                    {
                        progress?.Report($"  Overwriting existing: {Path.GetFileName(finalMdPath)}");
                    }
                }
                else
                {
                    // Find a unique filename
                    int counter = 1;
                    while (File.Exists(finalMdPath))
                    {
                        finalMdPath = ExportPathSanitizer.GetSafeMarkdownFilePath(folderPath, page.Name, page.Id, counter);
                        counter++;
                    }
                }
            }

            try
            {
                // Get page content directly via XML (bypasses DLP/Publish restrictions)
                var pageXml = _oneNoteService.GetPageContent(page.Id);

                var relativeAssetsPath = AssetPathResolver.GetRelativeAssetsPath(folderPath, assetsRoot);

                // Create a binary content fetcher for images that aren't embedded
                BinaryContentFetcher binaryFetcher = (callbackId) => _oneNoteService.GetBinaryPageContent(page.Id, callbackId);

                // Convert XML directly to Markdown (no Publish API needed)
                // Use page name as prefix to avoid image filename collisions across pages
                var markdown = _xmlConverter.Convert(
                    pageXml,
                    assetsRoot,
                    relativeAssetsPath,
                    page.LastModifiedTime,
                    options.IncludeTimestamps,
                    binaryFetcher,
                    page.Name);

                // Apply linting if enabled (using markdownlint-cli)
                if (options.ApplyLinting)
                {
                    try
                    {
                        markdown = _cliLinter.LintContent(markdown);
                    }
                    catch (Exception lintEx)
                    {
                        progress?.Report($"  Warning: Linting failed for '{page.Name}': {lintEx.Message}");
                        // Continue with unlinted markdown
                    }
                }

                File.WriteAllText(finalMdPath, markdown);

                if (options.IncludeTimestamps && page.LastModifiedTime.HasValue)
                {
                    try
                    {
                        File.SetLastWriteTime(finalMdPath, page.LastModifiedTime.Value.LocalDateTime);
                    }
                    catch (Exception timeEx)
                    {
                        if (options.Verbose)
                        {
                            progress?.Report($"  Warning: Could not set last-write time for '{page.Name}': {timeEx.Message}");
                        }
                    }
                }

                result.ExportedPages++;

                if (options.Verbose)
                {
                    progress?.Report($"  Saved: {finalMdPath}");
                }
            }
            catch (Exception ex)
            {
                result.FailedPages++;
                progress?.Report($"  Error exporting '{page.Name}': {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Result of an export operation.
    /// </summary>
    public class ExportResult
    {
        public int TotalItems { get; set; }
        public int ExportedPages { get; set; }
        public int FailedPages { get; set; }
        public string? Error { get; set; }
        public bool Success => string.IsNullOrEmpty(Error) && FailedPages == 0;
    }
}
