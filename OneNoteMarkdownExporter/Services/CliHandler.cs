using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
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
            return await rootCommand.InvokeAsync(args);
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
                "--overwrite", "--no-lint", "--use-markdown-cli-linter", "--lint-config",
                "--list", "--dry-run", "--verbose", "-v", "--quiet", "-q",
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
            var allOption = new Option<bool>(
                "--all",
                "Export all notebooks");

            var notebookOption = new Option<string[]>(
                "--notebook",
                "Export specific notebook(s) by name")
            {
                AllowMultipleArgumentsPerToken = false
            };

            var sectionOption = new Option<string[]>(
                "--section",
                "Export section(s) by path (e.g., 'Notebook/Section')")
            {
                AllowMultipleArgumentsPerToken = false
            };

            var pageOption = new Option<string[]>(
                "--page",
                "Export page(s) by ID")
            {
                AllowMultipleArgumentsPerToken = false
            };

            var outputOption = new Option<string>(
                aliases: new[] { "--output", "-o" },
                description: "Output directory for exported files",
                getDefaultValue: ExportOptions.GetDefaultOutputPath);

            var overwriteOption = new Option<bool>(
                "--overwrite",
                "Overwrite existing files instead of creating numbered copies");

            var noLintOption = new Option<bool>(
                "--no-lint",
                "Disable Markdown linting");

            var useCliLinterOption = new Option<bool>(
                "--use-markdown-cli-linter",
                "Use markdownlint-cli (with --fix) instead of built-in linter");

            var lintConfigOption = new Option<string?>(
                "--lint-config",
                "Path to custom markdownlint configuration file");

            var listOption = new Option<bool>(
                "--list",
                "List available notebooks, sections, and pages without exporting");

            var dryRunOption = new Option<bool>(
                "--dry-run",
                "Preview what would be exported without actually exporting");

            var verboseOption = new Option<bool>(
                aliases: new[] { "--verbose", "-v" },
                "Show detailed output");

            var quietOption = new Option<bool>(
                aliases: new[] { "--quiet", "-q" },
                "Show only errors");

            // Add options to command
            rootCommand.AddOption(allOption);
            rootCommand.AddOption(notebookOption);
            rootCommand.AddOption(sectionOption);
            rootCommand.AddOption(pageOption);
            rootCommand.AddOption(outputOption);
            rootCommand.AddOption(overwriteOption);
            rootCommand.AddOption(noLintOption);
            rootCommand.AddOption(useCliLinterOption);
            rootCommand.AddOption(lintConfigOption);
            rootCommand.AddOption(listOption);
            rootCommand.AddOption(dryRunOption);
            rootCommand.AddOption(verboseOption);
            rootCommand.AddOption(quietOption);

            rootCommand.SetHandler(async (context) =>
            {
                var result = context.ParseResult;

                var options = new ExportOptions
                {
                    ExportAll = result.GetValueForOption(allOption),
                    NotebookNames = result.GetValueForOption(notebookOption)?.ToList(),
                    SectionPaths = result.GetValueForOption(sectionOption)?.ToList(),
                    PageIds = result.GetValueForOption(pageOption)?.ToList(),
                    OutputPath = result.GetValueForOption(outputOption) ?? ExportOptions.GetDefaultOutputPath(),
                    Overwrite = result.GetValueForOption(overwriteOption),
                    ApplyLinting = !result.GetValueForOption(noLintOption),
                    UseMarkdownCliLinter = result.GetValueForOption(useCliLinterOption),
                    LintConfigPath = result.GetValueForOption(lintConfigOption),
                    DryRun = result.GetValueForOption(dryRunOption),
                    Verbose = result.GetValueForOption(verboseOption),
                    Quiet = result.GetValueForOption(quietOption)
                };

                var listMode = result.GetValueForOption(listOption);

                var exitCode = await ExecuteAsync(options, listMode, context.GetCancellationToken());
                context.ExitCode = exitCode;
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

                // Check CLI linter availability
                if (options.UseMarkdownCliLinter && !exportService.IsMarkdownCliLinterAvailable)
                {
                    Console.Error.WriteLine($"Warning: markdownlint-cli is unavailable: {exportService.MarkdownCliLinterUnavailableReason}");
                    Console.Error.WriteLine("Falling back to built-in linter.");
                    options.UseMarkdownCliLinter = false;
                }

                // Report configuration
                if (!options.Quiet)
                {
                    Console.WriteLine("OneNote to Markdown Exporter");
                    Console.WriteLine("============================");
                    Console.WriteLine($"Output directory: {options.OutputPath}");
                    Console.WriteLine($"Overwrite: {(options.Overwrite ? "Yes" : "No")}");
                    Console.WriteLine($"Linting: {(options.ApplyLinting ? (options.UseMarkdownCliLinter ? "markdownlint-cli" : "Built-in") : "Disabled")}");
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
