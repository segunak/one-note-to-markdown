using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OneNoteMarkdownExporter.Models;

namespace OneNoteMarkdownExporter.Services
{
    /// <summary>
    /// Handles command-line interface parsing and execution.
    /// </summary>
    public static class CliHandler
    {
        /// <summary>
        /// Parses command-line arguments and runs in CLI mode.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>Exit code (0 for success, non-zero for failure).</returns>
        public static async Task<int> RunAsync(string[] args)
        {
            var rootCommand = BuildRootCommand();
            return await rootCommand.Parse(args).InvokeAsync();
        }

        /// <summary>
        /// Checks if CLI mode should be activated based on arguments.
        /// </summary>
        public static bool ShouldRunCli(string[] args)
        {
            // If there are any command-line arguments, run in CLI mode
            // Exceptions: arguments that VS/Windows might pass when launching GUI
            if (args.Length == 0) return false;

            // Check for known CLI flags
            var cliFlags = new[]
            {
                "--all", "--notebook", "--section", "--page", "--output", "-o",
                "--assets-folder", "--overwrite", "--no-lint", "--lint-config",
                "--no-timestamps", "--list", "--dry-run", "--verbose", "-v", "--quiet", "-q",
                "--help", "-h", "-?", "--version"
            };

            return args.Any(arg => cliFlags.Any(flag => 
                arg.StartsWith(flag, StringComparison.OrdinalIgnoreCase)));
        }

        private static RootCommand BuildRootCommand()
        {
            var rootCommand = new RootCommand("OneNote to Markdown Exporter - Export OneNote pages to Markdown files.")
            {
                TreatUnmatchedTokensAsErrors = true
            };

            // Options
            var allOption = new Option<bool>("--all")
            {
                Description = "Export all notebooks"
            };

            var notebookOption = new Option<string[]>("--notebook")
            {
                Description = "Export specific notebook(s) by name",
                AllowMultipleArgumentsPerToken = false
            };

            var sectionOption = new Option<string[]>("--section")
            {
                Description = "Export section(s) by path (e.g., 'Notebook/Section')",
                AllowMultipleArgumentsPerToken = false
            };

            var pageOption = new Option<string[]>("--page")
            {
                Description = "Export page(s) by ID",
                AllowMultipleArgumentsPerToken = false
            };

            var outputOption = new Option<string>("--output", "-o")
            {
                Description = "Output directory for exported files",
                DefaultValueFactory = _ => ExportOptions.GetDefaultOutputPath()
            };

            var assetsFolderOption = new Option<string?>("--assets-folder")
            {
                Description = "Path to folder for storing exported assets (default: <output>/assets)"
            };

            var overwriteOption = new Option<bool>("--overwrite")
            {
                Description = "Overwrite existing files instead of creating numbered copies"
            };

            var noLintOption = new Option<bool>("--no-lint")
            {
                Description = "Disable Markdown linting (markdownlint-cli)"
            };

            var lintConfigOption = new Option<string?>("--lint-config")
            {
                Description = "Path to custom markdownlint configuration file"
            };

            var noTimestampsOption = new Option<bool>("--no-timestamps")
            {
                Description = "Do not include the OneNote page last-modified timestamp in the exported Markdown or file metadata"
            };

            var listOption = new Option<bool>("--list")
            {
                Description = "List available notebooks, sections, and pages without exporting"
            };

            var dryRunOption = new Option<bool>("--dry-run")
            {
                Description = "Preview what would be exported without actually exporting"
            };

            var verboseOption = new Option<bool>("--verbose", "-v")
            {
                Description = "Show detailed output"
            };

            var quietOption = new Option<bool>("--quiet", "-q")
            {
                Description = "Show only errors"
            };

            // Add options to command
            rootCommand.Options.Add(allOption);
            rootCommand.Options.Add(notebookOption);
            rootCommand.Options.Add(sectionOption);
            rootCommand.Options.Add(pageOption);
            rootCommand.Options.Add(outputOption);
            rootCommand.Options.Add(assetsFolderOption);
            rootCommand.Options.Add(overwriteOption);
            rootCommand.Options.Add(noLintOption);
            rootCommand.Options.Add(lintConfigOption);
            rootCommand.Options.Add(noTimestampsOption);
            rootCommand.Options.Add(listOption);
            rootCommand.Options.Add(dryRunOption);
            rootCommand.Options.Add(verboseOption);
            rootCommand.Options.Add(quietOption);

            rootCommand.SetAction(async (result, cancellationToken) =>
            {
                var options = new ExportOptions
                {
                    ExportAll = result.GetValue(allOption),
                    NotebookNames = result.GetValue(notebookOption)?.ToList(),
                    SectionPaths = result.GetValue(sectionOption)?.ToList(),
                    PageIds = result.GetValue(pageOption)?.ToList(),
                    OutputPath = result.GetValue(outputOption) ?? ExportOptions.GetDefaultOutputPath(),
                    AssetsFolderPath = result.GetValue(assetsFolderOption),
                    Overwrite = result.GetValue(overwriteOption),
                    ApplyLinting = !result.GetValue(noLintOption),
                    LintConfigPath = result.GetValue(lintConfigOption),
                    IncludeTimestamps = !result.GetValue(noTimestampsOption),
                    DryRun = result.GetValue(dryRunOption),
                    Verbose = result.GetValue(verboseOption),
                    Quiet = result.GetValue(quietOption)
                };

                var listMode = result.GetValue(listOption);

                return await ExecuteAsync(options, listMode, cancellationToken);
            });

            return rootCommand;
        }

        private static async Task<int> ExecuteAsync(ExportOptions options, bool listMode, CancellationToken cancellationToken)
        {
            var exportService = new ExportService();

            // Console progress reporter
            var progress = new Progress<string>(message =>
            {
                if (!options.Quiet || message.Contains("Error") || message.Contains("failed"))
                {
                    Console.WriteLine(message);
                }
            });

            try
            {
                // List mode - just show hierarchy
                if (listMode)
                {
                    return ListHierarchy(exportService, options.Verbose);
                }

                // Validate that we have selection criteria
                if (!options.HasSelectionCriteria())
                {
                    Console.Error.WriteLine("Error: No selection criteria specified.");
                    Console.Error.WriteLine("Use --all, --notebook, --section, or --page to specify what to export.");
                    Console.Error.WriteLine("Use --list to see available items.");
                    Console.Error.WriteLine("Use --help for more information.");
                    return 1;
                }

                // Report configuration
                if (!options.Quiet)
                {
                    Console.WriteLine("OneNote to Markdown Exporter");
                    Console.WriteLine("============================");
                    Console.WriteLine($"Output directory: {options.OutputPath}");
                    Console.WriteLine($"Assets directory: {AssetPathResolver.ResolveAssetsFolderPath(options.OutputPath, options.AssetsFolderPath)}");
                    Console.WriteLine($"Overwrite: {(options.Overwrite ? "Yes" : "No")}");
                    Console.WriteLine($"Linting: {(options.ApplyLinting ? "Enabled (markdownlint-cli)" : "Disabled")}");
                    Console.WriteLine($"Timestamps: {(options.IncludeTimestamps ? "Enabled" : "Disabled")}");
                    if (options.DryRun) Console.WriteLine("Mode: DRY RUN (no files will be created)");
                    Console.WriteLine();
                }

                // Run export
                var result = await exportService.ExportAsync(options, progress, cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                {
                    return 130; // Standard exit code for Ctrl+C
                }

                if (!string.IsNullOrEmpty(result.Error))
                {
                    Console.Error.WriteLine($"Export error: {result.Error}");
                    return 1;
                }

                // Summary
                if (!options.Quiet && !options.DryRun)
                {
                    Console.WriteLine();
                    Console.WriteLine("Export Summary:");
                    Console.WriteLine($"  Pages exported: {result.ExportedPages}");
                    if (result.FailedPages > 0)
                    {
                        Console.WriteLine($"  Pages failed: {result.FailedPages}");
                    }
                }

                return result.FailedPages > 0 ? 1 : 0;
            }
            catch (System.Runtime.InteropServices.COMException ex)
            {
                Console.Error.WriteLine($"OneNote COM error: {ex.Message}");
                Console.Error.WriteLine("Make sure OneNote is installed and not running in a protected mode.");
                return 2;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                if (options.Verbose)
                {
                    Console.Error.WriteLine(ex.StackTrace);
                }
                return 1;
            }
        }

        private static int ListHierarchy(ExportService exportService, bool verbose)
        {
            try
            {
                Console.WriteLine("OneNote Hierarchy");
                Console.WriteLine("=================");
                Console.WriteLine();

                var notebooks = exportService.GetNotebookHierarchy();

                if (notebooks.Count == 0)
                {
                    Console.WriteLine("No notebooks found.");
                    return 0;
                }

                foreach (var notebook in notebooks)
                {
                    PrintItem(notebook, "", verbose);
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error listing hierarchy: {ex.Message}");
                return 1;
            }
        }

        private static void PrintItem(OneNoteItem item, string indent, bool verbose)
        {
            var typeIcon = item.Type switch
            {
                OneNoteItemType.Notebook => "📓",
                OneNoteItemType.SectionGroup => "📁",
                OneNoteItemType.Section => "📄",
                OneNoteItemType.Page => "📝",
                _ => "❓"
            };

            var typeLabel = item.Type switch
            {
                OneNoteItemType.Notebook => "[Notebook]",
                OneNoteItemType.SectionGroup => "[SectionGroup]",
                OneNoteItemType.Section => "[Section]",
                OneNoteItemType.Page => "[Page]",
                _ => "[Unknown]"
            };

            if (verbose)
            {
                Console.WriteLine($"{indent}{typeIcon} {item.Name} {typeLabel}");
                Console.WriteLine($"{indent}   ID: {item.Id}");
            }
            else
            {
                Console.WriteLine($"{indent}{typeIcon} {item.Name}");
            }

            foreach (var child in item.Children)
            {
                PrintItem(child, indent + "  ", verbose);
            }
        }
    }
}
