using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using OneNoteMarkdownExporter.Models;
using OneNoteMarkdownExporter.Services;

namespace OneNoteMarkdownExporter
{
    public partial class MainWindow : Window
    {
        private OneNoteService? _oneNoteService;
        private MarkdownConverterService _markdownConverter;
        private OneNoteXmlToMarkdownConverter _xmlConverter;
        private MarkdownLintCliService _cliLinter;
        private CancellationTokenSource? _cts;
        private const string NoFailuresMessage = "No failures.";
        private int _failureLogCount;

        public MainWindow()
        {
            InitializeComponent();
            _markdownConverter = new MarkdownConverterService();
            _xmlConverter = new OneNoteXmlToMarkdownConverter();
            _cliLinter = new MarkdownLintCliService();
            Loaded += MainWindow_Loaded;
            
            // Subscribe to selection changes
            OneNoteItem.SelectionChanged += OnSelectionChanged;
            
            // Set default output path to Downloads\OneNoteExport
            SetDefaultOutputPath();
        }

        private void SetDefaultOutputPath()
        {
            try
            {
                // Get the Downloads folder path using Known Folder GUID
                string downloadsPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                downloadsPath = Path.Combine(downloadsPath, "Downloads", "OneNoteExport");
                
                // Create the directory if it doesn't exist
                if (!Directory.Exists(downloadsPath))
                {
                    Directory.CreateDirectory(downloadsPath);
                }
                
                OutputPathBox.Text = downloadsPath;
                AssetsPathBox.Text = AssetPathResolver.GetDefaultAssetsFolderPath(downloadsPath);
            }
            catch (Exception ex)
            {
                // Fallback to temp directory if Downloads fails
                string fallbackPath = Path.Combine(Path.GetTempPath(), "OneNoteExport");
                if (!Directory.Exists(fallbackPath))
                {
                    Directory.CreateDirectory(fallbackPath);
                }
                OutputPathBox.Text = fallbackPath;
                AssetsPathBox.Text = AssetPathResolver.GetDefaultAssetsFolderPath(fallbackPath);
                Log($"Could not set Downloads folder, using temp folder: {ex.Message}");
            }
        }

        private void OnSelectionChanged(object? sender, EventArgs e)
        {
            // Marshal to UI thread since this can be called from background thread during export
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(UpdateSelectionStatus);
            }
            else
            {
                UpdateSelectionStatus();
            }
        }

        private void UpdateSelectionStatus()
        {
            var items = NotebookTree.ItemsSource as List<OneNoteItem>;
            int selectedCount = items != null ? CountSelectedItems(items) : 0;
            
            if (selectedCount > 0)
            {
                // Ready to export - green/success state
                SelectionStatusBorder.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#D4EDDA"));
                SelectionStatusBorder.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#C3E6CB"));
                SelectionStatusIcon.Text = "✓";
                SelectionStatusText.Text = $"{selectedCount} item{(selectedCount == 1 ? "" : "s")} selected for export";
                ExportButton.IsEnabled = true;
            }
            else
            {
                // Nothing selected - warning state
                SelectionStatusBorder.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFF3CD"));
                SelectionStatusBorder.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFECB5"));
                SelectionStatusIcon.Text = "⚠";
                SelectionStatusText.Text = "Select notebooks, sections, or pages from the tree to export";
                ExportButton.IsEnabled = false;
            }
        }

        private int CountSelectedItems(List<OneNoteItem> items)
        {
            int count = 0;
            foreach (var item in items)
            {
                if (item.IsSelected) count++;
                count += CountSelectedItems(item.Children);
            }
            return count;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadNotebooks();
        }

        private void LoadNotebooks()
        {
            try
            {
                _oneNoteService = new OneNoteService();
                var items = _oneNoteService.GetNotebookHierarchy();
                NotebookTree.ItemsSource = items;
                Log("Notebooks loaded successfully.");
                UpdateSelectionStatus();
            }
            catch (Exception ex)
            {
                Log($"Error loading notebooks: {ex.Message}");
                System.Windows.MessageBox.Show("Error loading OneNote. Make sure OneNote Desktop is running.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadNotebooks();
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    var previousDefaultAssetsPath = AssetPathResolver.GetDefaultAssetsFolderPath(OutputPathBox.Text);
                    OutputPathBox.Text = dialog.SelectedPath;
                    if (string.IsNullOrWhiteSpace(AssetsPathBox.Text) || PathsEqual(AssetsPathBox.Text, previousDefaultAssetsPath))
                    {
                        AssetsPathBox.Text = AssetPathResolver.GetDefaultAssetsFolderPath(dialog.SelectedPath);
                    }
                }
            }
        }

        private void BrowseAssetsButton_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                if (!string.IsNullOrWhiteSpace(AssetsPathBox.Text) && Directory.Exists(AssetsPathBox.Text))
                {
                    dialog.SelectedPath = AssetsPathBox.Text;
                }

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    AssetsPathBox.Text = dialog.SelectedPath;
                }
            }
        }

        private async void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            var rootPath = OutputPathBox.Text;
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                System.Windows.MessageBox.Show("Please select an output directory.");
                return;
            }

            var items = NotebookTree.ItemsSource as List<OneNoteItem>;
            if (items == null) return;

            var totalPages = ExportSelectionHelper.CountPagesToExport(items);
            if (totalPages == 0)
            {
                ExportProgressBar.IsIndeterminate = false;
                ExportProgressBar.Value = 0;
                SetExportStatus("No pages selected for export.", "#856404");
                Log("No pages selected for export.");
                return;
            }

            ExportButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            NotebookTree.IsEnabled = false;
            RefreshButton.IsEnabled = false;
            ResetFailureLog();
            ExportProgressBar.Minimum = 0;
            ExportProgressBar.Maximum = totalPages;
            ExportProgressBar.Value = 0;
            ExportProgressBar.IsIndeterminate = false;
            SetExportStatus($"Exported 0 of {totalPages} pages...", "#0C5460");
            Log("Starting export...");

            bool expandCollapsed = ExpandCollapsedBox.IsChecked == true;
            bool overwriteExisting = OverwriteExistingBox.IsChecked == true;
            bool applyLinting = LintMarkdownBox.IsChecked == true;
            bool includeTimestamps = IncludeTimestampsBox.IsChecked == true;
            string assetsRoot;
            try
            {
                assetsRoot = AssetPathResolver.PrepareAssetsFolder(rootPath, AssetsPathBox.Text);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Invalid assets folder: {ex.Message}", "Assets Folder", MessageBoxButton.OK, MessageBoxImage.Error);
                ExportButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                NotebookTree.IsEnabled = true;
                RefreshButton.IsEnabled = true;
                ExportProgressBar.IsIndeterminate = false;
                ExportProgressBar.Value = 0;
                LogGeneralFailure("Preparing assets folder", ex);
                ExportLogTabs.SelectedItem = FailureLogTab;
                SetExportStatus("Export failed.", "#721C24");
                return;
            }

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            var exportFailed = false;
            var progressState = new ExportProgressState(totalPages);

            if (applyLinting)
            {
                Log("Markdown linting enabled (markdownlint-cli)");
            }

            await Task.Run(() =>
            {
                try
                {
                    foreach (var item in items)
                    {
                        if (token.IsCancellationRequested) break;
                        ExportItem(item, rootPath, rootPath, assetsRoot, expandCollapsed, overwriteExisting, applyLinting, includeTimestamps, token, progressState);
                    }
                    
                    if (token.IsCancellationRequested)
                    {
                        Dispatcher.Invoke(() => Log("Export stopped by user."));
                    }
                    else
                    {
                        Dispatcher.Invoke(() => Log("Export completed successfully!"));
                    }
                }
                catch (Exception ex)
                {
                    exportFailed = true;
                    Dispatcher.Invoke(() => LogGeneralFailure("Export", ex));
                }
            });

            ExportButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            NotebookTree.IsEnabled = true;
            RefreshButton.IsEnabled = true;
            ExportProgressBar.IsIndeterminate = false;
            if (token.IsCancellationRequested)
            {
                ExportProgressBar.Value = progressState.ExportedPages;
                SetExportStatus($"Export stopped. {progressState.ExportedPages} of {totalPages} pages exported.", "#856404");
            }
            else if (exportFailed || progressState.FailedPages > 0)
            {
                ExportProgressBar.Value = progressState.ExportedPages;
                if (progressState.FailedPages > 0)
                {
                    SetExportStatus($"Export finished with errors. {progressState.ExportedPages} of {totalPages} pages exported, {progressState.FailedPages} failed.", "#721C24");
                }
                else
                {
                    SetExportStatus($"Export failed. {progressState.ExportedPages} of {totalPages} pages exported.", "#721C24");
                }
            }
            else
            {
                ExportProgressBar.Value = progressState.TotalPages;
                SetExportStatus($"Export completed successfully. {progressState.ExportedPages} of {totalPages} pages exported.", "#155724");
            }

            if (_failureLogCount > 0)
            {
                ExportLogTabs.SelectedItem = FailureLogTab;
            }

            UpdateSelectionStatus();
            _cts = null;
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                Log("Stopping export... please wait for current operation to finish.");
                StopButton.IsEnabled = false;
            }
        }

        private void ConfigureLinting_Click(object sender, RoutedEventArgs e)
        {
            // Open the .markdownlint.json config file
            var configPath = Path.Combine(AppContext.BaseDirectory, "resources", ".markdownlint.json");
            if (File.Exists(configPath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = configPath,
                        UseShellExecute = true
                    });
                    Log($"Opened config file: {configPath}");
                }
                catch (Exception ex)
                {
                    Log($"Error opening config file: {ex.Message}");
                }
            }
            else
            {
                System.Windows.MessageBox.Show(
                    $"Config file not found at: {configPath}",
                    "Configuration",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void ExportItem(OneNoteItem item, string currentPath, string rootPath, string assetsRoot, bool expandCollapsed, bool overwriteExisting, bool applyLinting, bool includeTimestamps, CancellationToken token, ExportProgressState progressState, bool isImplicitlySelected = false)
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

                    ExportItem(child, myPath, rootPath, assetsRoot, expandCollapsed, overwriteExisting, applyLinting, includeTimestamps, token, progressState, isSelected);
                }
            }
            else
            {
                // It's a page
                if (isSelected)
                {
                    var exported = ExportPage(item, currentPath, rootPath, assetsRoot, expandCollapsed, overwriteExisting, applyLinting, includeTimestamps, token);
                    if (exported)
                    {
                        progressState.ExportedPages++;
                    }
                    else if (!token.IsCancellationRequested)
                    {
                        progressState.FailedPages++;
                    }

                    Dispatcher.Invoke(() => UpdateExportProgress(progressState));
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

                        ExportItem(child, myPath, rootPath, assetsRoot, expandCollapsed, overwriteExisting, applyLinting, includeTimestamps, token, progressState, isSelected);
                    }
                }
            }
        }

        private void UpdateExportProgress(ExportProgressState progressState)
        {
            ExportProgressBar.Value = progressState.ExportedPages;
            var failedText = progressState.FailedPages > 0 ? $", {progressState.FailedPages} failed" : "";
            SetExportStatus($"Exported {progressState.ExportedPages} of {progressState.TotalPages} pages{failedText}...", "#0C5460");
        }

        private void SetExportStatus(string message, string colorHex)
        {
            ExportStatusText.Text = message;
            ExportStatusText.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex));
        }

        private void ResetFailureLog()
        {
            _failureLogCount = 0;
            FailureLogTab.Header = "Failures (0)";
            FailureLogBox.Text = NoFailuresMessage;
            ExportLogTabs.SelectedItem = AllLogsTab;
        }

        private void LogPageFailure(OneNoteItem page, string targetPath, Exception ex)
        {
            var failureDetails = ExportFailureFormatter.FormatPageFailure(page, targetPath, ex);
            Log($"Export failure:\n{failureDetails}");
            AppendFailureEntry(failureDetails);
        }

        private void LogGeneralFailure(string operation, Exception ex)
        {
            var failureDetails = ExportFailureFormatter.FormatGeneralFailure(operation, ex);
            Log($"Export failure:\n{failureDetails}");
            AppendFailureEntry(failureDetails);
        }

        private void AppendFailureEntry(string failureDetails)
        {
            if (!FailureLogBox.Dispatcher.CheckAccess())
            {
                FailureLogBox.Dispatcher.Invoke(() => AppendFailureEntry(failureDetails));
                return;
            }

            if (FailureLogBox.Text == NoFailuresMessage)
            {
                FailureLogBox.Clear();
            }

            _failureLogCount++;
            FailureLogTab.Header = $"Failures ({_failureLogCount})";
            FailureLogBox.AppendText($"{DateTime.Now:HH:mm:ss}: {failureDetails}{Environment.NewLine}{Environment.NewLine}");
            FailureLogBox.ScrollToEnd();
        }

        private bool ExportPage(OneNoteItem page, string folderPath, string rootPath, string assetsRoot, bool expandCollapsed, bool overwriteExisting, bool applyLinting, bool includeTimestamps, CancellationToken token)
        {
            if (_oneNoteService == null) return false;
            if (token.IsCancellationRequested) return false;

            Dispatcher.Invoke(() => Log($"Exporting Page: {page.Name}"));

            Directory.CreateDirectory(folderPath);
            var finalMdPath = ExportPathSanitizer.GetSafeMarkdownFilePath(folderPath, page.Name, page.Id);
            
            // Handle file existence based on overwrite setting
            if (File.Exists(finalMdPath))
            {
                if (overwriteExisting)
                {
                    // Will overwrite below
                    Dispatcher.Invoke(() => Log($"  Overwriting existing: {Path.GetFileName(finalMdPath)}"));
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
                    includeTimestamps,
                    binaryFetcher,
                    page.Name);
                
                // Apply linting if enabled (using markdownlint-cli)
                if (applyLinting)
                {
                    try
                    {
                        markdown = _cliLinter.LintContent(markdown);
                    }
                    catch (Exception lintEx)
                    {
                        Dispatcher.Invoke(() => Log($"  Warning: Linting failed for '{page.Name}': {lintEx.Message}"));
                        // Continue with unlinted markdown
                    }
                }
                
                File.WriteAllText(finalMdPath, markdown);

                if (includeTimestamps && page.LastModifiedTime.HasValue)
                {
                    try
                    {
                        File.SetLastWriteTime(finalMdPath, page.LastModifiedTime.Value.LocalDateTime);
                    }
                    catch (Exception timeEx)
                    {
                        Dispatcher.Invoke(() => Log($"  Warning: Could not set last-write time for '{page.Name}': {timeEx.Message}"));
                    }
                }
                
                Dispatcher.Invoke(() => Log($"  Exported successfully: {page.Name}"));
                return true;
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => LogPageFailure(page, finalMdPath, ex));
                return false;
            }
        }

        private class ExportProgressState
        {
            public ExportProgressState(int totalPages)
            {
                TotalPages = totalPages;
            }

            public int TotalPages { get; }
            public int ExportedPages { get; set; }
            public int FailedPages { get; set; }
        }

        private static bool PathsEqual(string firstPath, string secondPath)
        {
            try
            {
                return string.Equals(
                    Path.GetFullPath(firstPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    Path.GetFullPath(secondPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(firstPath, secondPath, StringComparison.OrdinalIgnoreCase);
            }
        }

        private void Log(string message)
        {
            if (LogBox.Dispatcher.CheckAccess())
            {
                LogBox.AppendText($"{DateTime.Now:HH:mm:ss}: {message}\n");
                LogBox.ScrollToEnd();
            }
            else
            {
                LogBox.Dispatcher.Invoke(() => Log(message));
            }
        }
    }
}