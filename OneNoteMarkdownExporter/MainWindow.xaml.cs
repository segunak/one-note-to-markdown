using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms; // For FolderBrowserDialog
using OneNoteMarkdownExporter.Models;
using OneNoteMarkdownExporter.Services;

namespace OneNoteMarkdownExporter
{
    public partial class MainWindow : Window
    {
        private OneNoteService? _oneNoteService;
        private MarkdownConverterService _markdownConverter;
        private OneNoteXmlToMarkdownConverter _xmlConverter;
        private MarkdownLinter _linter;
        private LintOptions _lintOptions;
        private CancellationTokenSource? _cts;

        public MainWindow()
        {
            InitializeComponent();
            _markdownConverter = new MarkdownConverterService();
            _xmlConverter = new OneNoteXmlToMarkdownConverter();
            _lintOptions = LintOptions.CreateDefault();
            _linter = new MarkdownLinter(_lintOptions);
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
                    OutputPathBox.Text = dialog.SelectedPath;
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

            ExportButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            ExportProgressBar.IsIndeterminate = true;
            Log("Starting export...");

            bool expandCollapsed = ExpandCollapsedBox.IsChecked == true;
            bool overwriteExisting = OverwriteExistingBox.IsChecked == true;
            bool applyLinting = LintMarkdownBox.IsChecked == true;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            await Task.Run(() =>
            {
                try
                {
                    foreach (var item in items)
                    {
                        if (token.IsCancellationRequested) break;
                        ExportItem(item, rootPath, rootPath, expandCollapsed, overwriteExisting, applyLinting, token);
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
                    Dispatcher.Invoke(() => Log($"Export failed: {ex.Message}"));
                }
            });

            ExportButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            ExportProgressBar.IsIndeterminate = false;
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
            var configWindow = new LintConfigWindow(_lintOptions);
            configWindow.Owner = this;
            
            if (configWindow.ShowDialog() == true && configWindow.WasSaved)
            {
                _lintOptions = configWindow.Options;
                _linter = new MarkdownLinter(_lintOptions);
                Log("Linting rules updated.");
            }
        }

        private void ExportItem(OneNoteItem item, string currentPath, string rootPath, bool expandCollapsed, bool overwriteExisting, bool applyLinting, CancellationToken token)
        {
            if (token.IsCancellationRequested) return;

            bool isSelected = item.IsSelected;
            bool hasSelectedDescendants = HasSelectedDescendants(item);

            if (!isSelected && !hasSelectedDescendants) return;

            string myPath = currentPath;
            
            // Sanitize name
            var safeName = string.Join("_", item.Name.Split(Path.GetInvalidFileNameChars()));
            
            if (item.Type != OneNoteItemType.Page)
            {
                // It's a container
                myPath = Path.Combine(currentPath, safeName);
                if (!Directory.Exists(myPath))
                {
                    Directory.CreateDirectory(myPath);
                }

                foreach (var child in item.Children)
                {
                    if (token.IsCancellationRequested) return;

                    // If parent (this item) is selected, treat child as selected
                    if (isSelected) child.IsSelected = true; 
                    ExportItem(child, myPath, rootPath, expandCollapsed, overwriteExisting, applyLinting, token);
                }
            }
            else
            {
                // It's a page
                if (isSelected)
                {
                    ExportPage(item, currentPath, rootPath, expandCollapsed, overwriteExisting, applyLinting, token);
                }
            }
        }

        private bool HasSelectedDescendants(OneNoteItem item)
        {
            foreach (var child in item.Children)
            {
                if (child.IsSelected || HasSelectedDescendants(child)) return true;
            }
            return false;
        }

        private void ExportPage(OneNoteItem page, string folderPath, string rootPath, bool expandCollapsed, bool overwriteExisting, bool applyLinting, CancellationToken token)
        {
            if (_oneNoteService == null) return;
            if (token.IsCancellationRequested) return;

            Dispatcher.Invoke(() => Log($"Exporting Page: {page.Name}"));

            var safeName = string.Join("_", page.Name.Split(Path.GetInvalidFileNameChars()));
            
            var finalMdPath = Path.Combine(folderPath, $"{safeName}.md");
            
            // Handle file existence based on overwrite setting
            if (File.Exists(finalMdPath))
            {
                if (overwriteExisting)
                {
                    // Will overwrite below
                    Dispatcher.Invoke(() => Log($"  Overwriting existing: {safeName}.md"));
                }
                else
                {
                    // Find a unique filename
                    int counter = 1;
                    while (File.Exists(finalMdPath))
                    {
                        finalMdPath = Path.Combine(folderPath, $"{safeName} ({counter}).md");
                        counter++;
                    }
                }
            }

            var assetsRoot = Path.Combine(rootPath, "assets");

            try
            {
                // Get page content directly via XML (bypasses DLP/Publish restrictions)
                var pageXml = _oneNoteService.GetPageContent(page.Id);
                
                if (!Directory.Exists(assetsRoot)) Directory.CreateDirectory(assetsRoot);

                var relativeAssetsPath = Path.GetRelativePath(folderPath, assetsRoot).Replace("\\", "/");
                
                // Convert XML directly to Markdown (no Publish API needed)
                var markdown = _xmlConverter.Convert(pageXml, assetsRoot, relativeAssetsPath);
                
                // Apply linting if enabled
                if (applyLinting)
                {
                    markdown = _linter.Lint(markdown);
                }
                
                File.WriteAllText(finalMdPath, markdown);
                
                Dispatcher.Invoke(() => Log($"  Exported successfully: {page.Name}"));
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => Log($"  Error exporting page '{page.Name}': {ex.Message}"));
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