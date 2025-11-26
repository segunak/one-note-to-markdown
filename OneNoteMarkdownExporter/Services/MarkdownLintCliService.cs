using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace OneNoteMarkdownExporter.Services
{
    /// <summary>
    /// Service for running markdownlint-cli using the bundled Node.js runtime.
    /// This provides full markdownlint compatibility without requiring users to install Node.js.
    /// </summary>
    public class MarkdownLintCliService
    {
        private readonly string _nodeExePath;
        private readonly string _markdownLintPath;
        private readonly string _configPath;
        private bool _isAvailable;

        public bool IsAvailable => _isAvailable;
        public string UnavailableReason { get; private set; } = "";

        public MarkdownLintCliService()
        {
            // Get the path to the resources folder relative to the executable
            var baseDir = AppContext.BaseDirectory;
            var resourcesDir = Path.Combine(baseDir, "resources");

            _nodeExePath = Path.Combine(resourcesDir, "node.exe");
            _markdownLintPath = Path.Combine(resourcesDir, "node_modules", "markdownlint-cli", "markdownlint.js");
            _configPath = Path.Combine(resourcesDir, ".markdownlint.json");

            CheckAvailability();
        }

        private void CheckAvailability()
        {
            if (!File.Exists(_nodeExePath))
            {
                _isAvailable = false;
                UnavailableReason = $"node.exe not found at: {_nodeExePath}";
                return;
            }

            if (!File.Exists(_markdownLintPath))
            {
                _isAvailable = false;
                UnavailableReason = $"markdownlint-cli not found. Please run 'npm install' in the resources folder.";
                return;
            }

            _isAvailable = true;
            UnavailableReason = "";
        }

        /// <summary>
        /// Lints and fixes a markdown file in place using markdownlint-cli.
        /// </summary>
        /// <param name="filePath">Path to the markdown file to lint.</param>
        /// <returns>Result containing success status and any output messages.</returns>
        public async Task<LintResult> LintFileAsync(string filePath)
        {
            if (!_isAvailable)
            {
                return new LintResult
                {
                    Success = false,
                    ErrorMessage = UnavailableReason
                };
            }

            if (!File.Exists(filePath))
            {
                return new LintResult
                {
                    Success = false,
                    ErrorMessage = $"File not found: {filePath}"
                };
            }

            try
            {
                var arguments = new StringBuilder();
                arguments.Append($"\"{_markdownLintPath}\" ");
                arguments.Append("--fix ");
                
                // Use config file if it exists
                if (File.Exists(_configPath))
                {
                    arguments.Append($"--config \"{_configPath}\" ");
                }

                arguments.Append($"\"{filePath}\"");

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _nodeExePath,
                        Arguments = arguments.ToString(),
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = Path.GetDirectoryName(filePath) ?? ""
                    }
                };

                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();

                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        outputBuilder.AppendLine(e.Data);
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        errorBuilder.AppendLine(e.Data);
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync();

                var output = outputBuilder.ToString();
                var error = errorBuilder.ToString();

                // Exit code 0 = success, 1 = lint errors found (but --fix applied what it could)
                // Exit code > 1 = actual error
                if (process.ExitCode > 1)
                {
                    return new LintResult
                    {
                        Success = false,
                        ErrorMessage = error,
                        Output = output
                    };
                }

                return new LintResult
                {
                    Success = true,
                    Output = output,
                    WarningMessage = error // Lint warnings go to stderr
                };
            }
            catch (Exception ex)
            {
                return new LintResult
                {
                    Success = false,
                    ErrorMessage = $"Failed to run markdownlint-cli: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Lints markdown content (not a file) by writing to a temp file, linting, and reading back.
        /// </summary>
        /// <param name="markdown">The markdown content to lint.</param>
        /// <returns>The linted markdown content.</returns>
        public async Task<string> LintContentAsync(string markdown)
        {
            if (!_isAvailable)
            {
                // Return original content if markdownlint-cli is not available
                return markdown;
            }

            // Create a temporary file
            var tempFile = Path.Combine(Path.GetTempPath(), $"mdlint_{Guid.NewGuid():N}.md");

            try
            {
                // Write content to temp file
                await File.WriteAllTextAsync(tempFile, markdown);

                // Lint the file
                var result = await LintFileAsync(tempFile);

                if (result.Success)
                {
                    // Read back the linted content
                    return await File.ReadAllTextAsync(tempFile);
                }
                else
                {
                    // Return original content if linting failed
                    return markdown;
                }
            }
            finally
            {
                // Clean up temp file
                try
                {
                    if (File.Exists(tempFile))
                        File.Delete(tempFile);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }

        /// <summary>
        /// Synchronous version of LintContentAsync for compatibility.
        /// </summary>
        public string LintContent(string markdown)
        {
            return LintContentAsync(markdown).GetAwaiter().GetResult();
        }
    }

    public class LintResult
    {
        public bool Success { get; set; }
        public string Output { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
        public string WarningMessage { get; set; } = "";
    }
}
