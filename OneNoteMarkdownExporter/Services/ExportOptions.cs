using System.Collections.Generic;

namespace OneNoteMarkdownExporter.Services
{
    /// <summary>
    /// Options for controlling the export process.
    /// Used by both GUI and CLI modes.
    /// </summary>
    public class ExportOptions
    {
        /// <summary>
        /// Root directory where exported files will be saved.
        /// </summary>
        public string OutputPath { get; set; } = string.Empty;

        /// <summary>
        /// If true, overwrite existing files. If false, create numbered copies.
        /// </summary>
        public bool Overwrite { get; set; } = false;

        /// <summary>
        /// If true, apply Markdown linting/formatting to exported content.
        /// </summary>
        public bool ApplyLinting { get; set; } = true;

        /// <summary>
        /// If true, use markdownlint-cli. If false, use the built-in C# linter.
        /// Only relevant when ApplyLinting is true.
        /// </summary>
        public bool UseMarkdownCliLinter { get; set; } = false;

        /// <summary>
        /// Path to custom .markdownlint.json config file (for CLI linter).
        /// If null/empty, uses the default bundled config.
        /// </summary>
        public string? LintConfigPath { get; set; }

        /// <summary>
        /// Options for the built-in linter. If null, uses defaults.
        /// </summary>
        public LintOptions? BuiltInLintOptions { get; set; }

        // Selection options - at least one should be set for CLI mode

        /// <summary>
        /// If true, export all notebooks.
        /// </summary>
        public bool ExportAll { get; set; } = false;

        /// <summary>
        /// List of notebook names to export. Case-insensitive matching.
        /// </summary>
        public List<string>? NotebookNames { get; set; }

        /// <summary>
        /// List of section paths to export (format: "NotebookName/SectionName" or "NotebookName/SectionGroupName/SectionName").
        /// </summary>
        public List<string>? SectionPaths { get; set; }

        /// <summary>
        /// List of specific page IDs to export.
        /// </summary>
        public List<string>? PageIds { get; set; }

        /// <summary>
        /// If true, show what would be exported without actually exporting.
        /// </summary>
        public bool DryRun { get; set; } = false;

        /// <summary>
        /// If true, output verbose logging.
        /// </summary>
        public bool Verbose { get; set; } = false;

        /// <summary>
        /// If true, suppress all output except errors.
        /// </summary>
        public bool Quiet { get; set; } = false;

        /// <summary>
        /// Creates default export options.
        /// </summary>
        public static ExportOptions CreateDefault()
        {
            return new ExportOptions
            {
                ApplyLinting = true,
                UseMarkdownCliLinter = false,
                Overwrite = false,
                BuiltInLintOptions = LintOptions.CreateDefault()
            };
        }

        /// <summary>
        /// Gets the default output path (Downloads\OneNoteExport).
        /// </summary>
        public static string GetDefaultOutputPath()
        {
            string downloadsPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
            return System.IO.Path.Combine(downloadsPath, "Downloads", "OneNoteExport");
        }

        /// <summary>
        /// Checks if any selection criteria is specified.
        /// </summary>
        public bool HasSelectionCriteria()
        {
            return ExportAll ||
                   (NotebookNames != null && NotebookNames.Count > 0) ||
                   (SectionPaths != null && SectionPaths.Count > 0) ||
                   (PageIds != null && PageIds.Count > 0);
        }
    }
}
