using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OneNoteMarkdownExporter.Models;

namespace OneNoteMarkdownExporter.Services
{
    public enum ExportProgressKind
    {
        Message,
        PageStarted,
        PageExported,
        PageFailed,
        Warning,
        Completed,
        Cancelled
    }

    public sealed class ExportProgressUpdate
    {
        public ExportProgressKind Kind { get; init; }
        public string Message { get; init; } = string.Empty;
        public OneNoteItem? Page { get; init; }
        public string? TargetPath { get; init; }
        public string? FailureDetails { get; init; }
        public int TotalPages { get; init; }
        public int ExportedPages { get; init; }
        public int FailedPages { get; init; }
    }

    public interface IOneNoteExportSource
    {
        List<OneNoteItem> GetNotebookHierarchy();
        string GetPageContent(string pageId);
        string? GetBinaryPageContent(string pageId, string callbackId);
    }

    public interface IMarkdownContentConverter
    {
        string Convert(string pageXml, string assetsFolder, string relativeAssetsPath, BinaryContentFetcher? binaryContentFetcher = null, string? pagePrefix = null);
    }

    public interface IMarkdownLintService
    {
        bool IsAvailable { get; }
        string UnavailableReason { get; }
        LintResult LintContent(string markdown, string? configPath = null);
    }

    /// <summary>
    /// Service for exporting OneNote content to Markdown.
    /// This service is UI-independent and can be used by both GUI and CLI.
    /// </summary>
    public class ExportService
    {
        private readonly IOneNoteExportSource _oneNoteService;
        private readonly IMarkdownContentConverter _xmlConverter;
        private readonly IMarkdownLintService _cliLinter;
        private readonly IFileTimestampService _timestampService;
        private readonly IYamlFrontMatterService _yamlFrontMatterService;

        public ExportService()
            : this(new OneNoteService(), new OneNoteXmlToMarkdownConverter(), new MarkdownLintCliService(), new FileTimestampService(), new YamlFrontMatterService())
        {
        }

        public ExportService(
            IOneNoteExportSource oneNoteService,
            IMarkdownContentConverter xmlConverter,
            IMarkdownLintService cliLinter,
            IFileTimestampService? timestampService = null,
            IYamlFrontMatterService? yamlFrontMatterService = null)
        {
            _oneNoteService = oneNoteService;
            _xmlConverter = xmlConverter;
            _cliLinter = cliLinter;
            _timestampService = timestampService ?? new FileTimestampService();
            _yamlFrontMatterService = yamlFrontMatterService ?? new YamlFrontMatterService();
        }

        /// <summary>
        /// Gets the notebook hierarchy from OneNote.
        /// </summary>
        public List<OneNoteItem> GetNotebookHierarchy()
        {
            return _oneNoteService.GetNotebookHierarchy();
        }

        /// <summary>
        /// Checks if markdownlint-cli2 is available.
        /// </summary>
        public bool IsMarkdownCliLinterAvailable => _cliLinter.IsAvailable;

        /// <summary>
        /// Gets the reason why markdownlint-cli2 is unavailable.
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
            IProgress<ExportProgressUpdate>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new ExportResult();
            
            try
            {
                PrepareOptions(options);

                // Get notebook hierarchy
                Report(progress, ExportProgressKind.Message, "Loading OneNote hierarchy...");
                var notebooks = _oneNoteService.GetNotebookHierarchy();

                // Apply selection criteria
                var selectedItems = ApplySelectionCriteria(notebooks, options);

                return await ExportItemsAsync(selectedItems, options, progress, cancellationToken);
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                Report(progress, ExportProgressKind.Message, $"Export failed: {ex.Message}");
            }

            return result;
        }

        public async Task<ExportResult> ExportSelectedAsync(
            List<OneNoteItem> items,
            ExportOptions options,
            IProgress<ExportProgressUpdate>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new ExportResult();

            try
            {
                PrepareOptions(options);
                return await ExportItemsAsync(items, options, progress, cancellationToken);
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                Report(progress, ExportProgressKind.Message, $"Export failed: {ex.Message}");
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

            if (options.ApplyLinting && _cliLinter.IsAvailable)
            {
                try
                {
                    var lintResult = _cliLinter.LintContent(markdown, options.LintConfigPath);
                    if (lintResult.Success)
                    {
                        markdown = lintResult.Content;
                    }
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

        private static void PrepareOptions(ExportOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.OutputPath))
            {
                options.OutputPath = ExportOptions.GetDefaultOutputPath();
            }

            options.OutputPath = Path.GetFullPath(options.OutputPath);
            options.Validate();

            if (!Directory.Exists(options.OutputPath))
            {
                Directory.CreateDirectory(options.OutputPath);
            }
        }

        private async Task<ExportResult> ExportItemsAsync(
            List<OneNoteItem> selectedItems,
            ExportOptions options,
            IProgress<ExportProgressUpdate>? progress,
            CancellationToken cancellationToken)
        {
            var result = new ExportResult();

            if (!selectedItems.Any())
            {
                Report(progress, ExportProgressKind.Message, "No items match the selection criteria.");
                return result;
            }

            result.TotalItems = ExportSelectionHelper.CountItemsToExport(selectedItems);
            result.TotalPages = ExportSelectionHelper.CountPagesToExport(selectedItems);
            Report(progress, ExportProgressKind.Message, $"Found {result.TotalItems} item(s) to export.", result);

            if (options.DryRun)
            {
                Report(progress, ExportProgressKind.Message, "Dry run mode - listing items that would be exported:", result);
                ListItems(selectedItems, progress, result, "");
                return result;
            }

            if (options.ApplyLinting && !_cliLinter.IsAvailable)
            {
                var warning = $"Markdown linting is unavailable: {_cliLinter.UnavailableReason} Export will continue without linting.";
                result.Warnings.Add(warning);
                Report(progress, ExportProgressKind.Warning, warning, result);
            }

            var planner = new ExportPathPlanner(options.OutputPath, options);
            planner.ValidatePaths(selectedItems);
            var centralizedAssetsRoot = options.AssetOrganizationMode == AssetOrganizationMode.Centralized
                ? AssetPathResolver.ResolveAssetsFolderPath(options.OutputPath, options.AssetsFolderPath)
                : null;

            if (centralizedAssetsRoot != null)
            {
                ValidateAssetsFolderPath(centralizedAssetsRoot);
            }

            await Task.Run(() =>
            {
                foreach (var item in selectedItems)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    ExportItem(item, planner.OutputRoot, centralizedAssetsRoot, null, null, planner, options, result, progress, cancellationToken);
                }
            }, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                Report(progress, ExportProgressKind.Cancelled, "Export cancelled by user.", result);
            }
            else
            {
                Report(progress, ExportProgressKind.Completed, $"Export completed. {result.ExportedPages} page(s) exported, {result.FailedPages} failed.", result);
            }

            return result;
        }

        private void ListItems(List<OneNoteItem> items, IProgress<ExportProgressUpdate>? progress, ExportResult result, string indent, bool isImplicitlySelected = false)
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
                    Report(progress, ExportProgressKind.Message, $"{indent}{typeStr} {item.Name}", result);
                    ListItems(item.Children, progress, result, indent + "  ", isSelected);
                }
            }
        }

        private void ExportItem(
            OneNoteItem item,
            string currentPath,
            string? centralizedAssetsRoot,
            string? notebookAssetsFolder,
            string? sectionAssetsFolder,
            ExportPathPlanner planner,
            ExportOptions options,
            ExportResult result,
            IProgress<ExportProgressUpdate>? progress,
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
                myPath = planner.GetContainerFolderPath(currentPath, item);
                if (!Directory.Exists(myPath))
                {
                    Directory.CreateDirectory(myPath);
                }

                var childNotebookAssetsFolder = notebookAssetsFolder;
                var childSectionAssetsFolder = sectionAssetsFolder;

                if (item.Type == OneNoteItemType.Notebook && options.AssetOrganizationMode == AssetOrganizationMode.Notebook)
                {
                    childNotebookAssetsFolder = planner.GetScopedAssetsFolderPath(myPath, item);
                }

                if (item.Type == OneNoteItemType.Section && options.AssetOrganizationMode == AssetOrganizationMode.Section)
                {
                    childSectionAssetsFolder = planner.GetScopedAssetsFolderPath(myPath, item);
                }

                foreach (var child in item.Children)
                {
                    if (token.IsCancellationRequested) return;

                    ExportItem(child, myPath, centralizedAssetsRoot, childNotebookAssetsFolder, childSectionAssetsFolder, planner, options, result, progress, token, isSelected);
                }
            }
            else
            {
                // It's a page
                if (isSelected)
                {
                    var assetsFolderPath = GetAssetsFolderPathForPage(item, currentPath, centralizedAssetsRoot, notebookAssetsFolder, sectionAssetsFolder, planner, options);
                    var pageContext = planner.CreatePageContext(item, currentPath, assetsFolderPath);
                    ExportPage(pageContext, options, result, progress, token);
                }

                if (item.Children.Count > 0)
                {
                    myPath = planner.GetChildPageFolderPath(currentPath, item);
                    if (!Directory.Exists(myPath))
                    {
                        Directory.CreateDirectory(myPath);
                    }

                    foreach (var child in item.Children)
                    {
                        if (token.IsCancellationRequested) return;

                        ExportItem(child, myPath, centralizedAssetsRoot, notebookAssetsFolder, sectionAssetsFolder, planner, options, result, progress, token, isSelected);
                    }
                }
            }
        }

        private static string GetAssetsFolderPathForPage(
            OneNoteItem page,
            string markdownFolderPath,
            string? centralizedAssetsRoot,
            string? notebookAssetsFolder,
            string? sectionAssetsFolder,
            ExportPathPlanner planner,
            ExportOptions options)
        {
            return options.AssetOrganizationMode switch
            {
                AssetOrganizationMode.Centralized => centralizedAssetsRoot ?? planner.GetCentralizedAssetsFolderPath(),
                AssetOrganizationMode.Notebook => notebookAssetsFolder ?? planner.GetScopedAssetsFolderPath(markdownFolderPath, page),
                AssetOrganizationMode.Section => sectionAssetsFolder ?? planner.GetScopedAssetsFolderPath(markdownFolderPath, page),
                AssetOrganizationMode.Page => planner.GetScopedAssetsFolderPath(markdownFolderPath, page),
                _ => throw new InvalidOperationException($"Unsupported asset organization mode: {options.AssetOrganizationMode}")
            };
        }

        private void ExportPage(
            PageExportContext pageContext,
            ExportOptions options,
            ExportResult result,
            IProgress<ExportProgressUpdate>? progress,
            CancellationToken token)
        {
            if (token.IsCancellationRequested) return;

            var page = pageContext.Page;

            if (!options.Quiet)
            {
                Report(progress, ExportProgressKind.PageStarted, $"Exporting: {page.Name}", result, page, pageContext.MarkdownFilePath);
            }

            Directory.CreateDirectory(pageContext.MarkdownFolderPath);

            var finalMdPath = pageContext.MarkdownFilePath;

            // Handle file existence based on overwrite setting
            if (File.Exists(finalMdPath))
            {
                if (options.Overwrite)
                {
                    if (options.Verbose)
                    {
                        Report(progress, ExportProgressKind.Message, $"  Overwriting existing: {Path.GetFileName(finalMdPath)}", result, page, finalMdPath);
                    }
                }
                else
                {
                    // Find a unique filename
                    int counter = 1;
                    while (File.Exists(finalMdPath))
                    {
                        finalMdPath = ExportPathSanitizer.GetSafeMarkdownFilePath(pageContext.MarkdownFolderPath, page.Name, page.Id, counter);
                        counter++;
                    }
                }
            }

            try
            {
                ValidateAssetsFolderPath(pageContext.AssetsFolderPath);

                // Get page content directly via XML (bypasses DLP/Publish restrictions)
                var pageXml = _oneNoteService.GetPageContent(page.Id);

                // Create a binary content fetcher for images that aren't embedded
                BinaryContentFetcher binaryFetcher = (callbackId) => _oneNoteService.GetBinaryPageContent(page.Id, callbackId);

                // Convert XML directly to Markdown (no Publish API needed)
                // Use page name as prefix to avoid image filename collisions across pages
                var markdown = _xmlConverter.Convert(pageXml, pageContext.AssetsFolderPath, pageContext.RelativeAssetsPath, binaryFetcher, page.Name);

                if (options.DateMetadataMode == DateMetadataMode.Yaml)
                {
                    markdown = _yamlFrontMatterService.AddFrontMatter(markdown, page);
                }

                // Apply linting if enabled and the bundled runtime is available.
                if (options.ApplyLinting && _cliLinter.IsAvailable)
                {
                    try
                    {
                        var lintResult = _cliLinter.LintContent(markdown, options.LintConfigPath);
                        if (lintResult.Success)
                        {
                            markdown = lintResult.Content;
                            if (!string.IsNullOrWhiteSpace(lintResult.WarningMessage))
                            {
                                var warning = $"Warning: Markdown linting completed with unresolved issues for '{page.Name}': {lintResult.WarningMessage}";
                                result.Warnings.Add(warning);
                                Report(progress, ExportProgressKind.Warning, warning, result, page, finalMdPath);
                            }
                        }
                        else
                        {
                            var warning = $"Warning: Markdown linting failed for '{page.Name}': {lintResult.ErrorMessage}";
                            result.Warnings.Add(warning);
                            Report(progress, ExportProgressKind.Warning, warning, result, page, finalMdPath);
                        }
                    }
                    catch (Exception lintEx)
                    {
                        var warning = $"Warning: Markdown linting failed for '{page.Name}': {lintEx.Message}";
                        result.Warnings.Add(warning);
                        Report(progress, ExportProgressKind.Warning, warning, result, page, finalMdPath);
                    }
                }

                File.WriteAllText(finalMdPath, markdown);
                ApplyPageTimestamps(finalMdPath, page, options, result, progress);
                result.ExportedPages++;
                Report(progress, ExportProgressKind.PageExported, $"  Exported successfully: {page.Name}", result, page, finalMdPath);

                if (options.Verbose)
                {
                    Report(progress, ExportProgressKind.Message, $"  Saved: {finalMdPath}", result, page, finalMdPath);
                }
            }
            catch (Exception ex)
            {
                result.FailedPages++;
                var failureDetails = ExportFailureFormatter.FormatPageFailure(page, finalMdPath, ex);
                result.Failures.Add(failureDetails);
                Report(progress, ExportProgressKind.PageFailed, $"  Error exporting '{page.Name}': {ex.Message}", result, page, finalMdPath, failureDetails);
            }
        }

        private static void ValidateAssetsFolderPath(string assetsFolderPath)
        {
            if (File.Exists(assetsFolderPath))
            {
                throw new IOException($"Assets folder path points to an existing file: {assetsFolderPath}");
            }
        }

        private void ApplyPageTimestamps(
            string markdownFilePath,
            OneNoteItem page,
            ExportOptions options,
            ExportResult result,
            IProgress<ExportProgressUpdate>? progress)
        {
            if (!options.PreserveDates || (!page.CreatedTime.HasValue && !page.LastModifiedTime.HasValue))
            {
                return;
            }

            try
            {
                _timestampService.ApplyTimestamps(markdownFilePath, page.CreatedTime, page.LastModifiedTime);
            }
            catch (Exception ex)
            {
                var warning = $"Warning: Could not preserve dates for '{page.Name}': {ex.Message}";
                result.Warnings.Add(warning);
                Report(progress, ExportProgressKind.Warning, warning, result, page, markdownFilePath);
            }
        }

        private static void Report(
            IProgress<ExportProgressUpdate>? progress,
            ExportProgressKind kind,
            string message,
            ExportResult? result = null,
            OneNoteItem? page = null,
            string? targetPath = null,
            string? failureDetails = null)
        {
            progress?.Report(new ExportProgressUpdate
            {
                Kind = kind,
                Message = message,
                Page = page,
                TargetPath = targetPath,
                FailureDetails = failureDetails,
                TotalPages = result?.TotalPages ?? 0,
                ExportedPages = result?.ExportedPages ?? 0,
                FailedPages = result?.FailedPages ?? 0
            });
        }
    }

    /// <summary>
    /// Result of an export operation.
    /// </summary>
    public class ExportResult
    {
        public int TotalItems { get; set; }
        public int TotalPages { get; set; }
        public int ExportedPages { get; set; }
        public int FailedPages { get; set; }
        public string? Error { get; set; }
        public List<string> Failures { get; } = new();
        public List<string> Warnings { get; } = new();
        public bool Success => string.IsNullOrEmpty(Error) && FailedPages == 0;
    }
}
