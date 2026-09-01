using System.Diagnostics;
using System.IO;
using System.Text;
using FluentAssertions;
using OneNoteMarkdownExporter.Services;
using Xunit;

namespace OneNoteMarkdownExporter.Tests.Services
{
    /// <summary>
    /// Integration tests for the committed markdownlint-cli2 runtime.
    /// </summary>
    public class MarkdownLintCliServiceTests
    {
        [Fact]
        public void IsAvailable_ShouldBeTrue_WhenResourcesExist()
        {
            var service = new MarkdownLintCliService();

            service.IsAvailable.Should().BeTrue($"because resources should exist: {service.UnavailableReason}");
        }

        [Fact]
        public void BundledNode_ShouldBePinnedVersion()
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(GetResourcesPath(), "node.exe"),
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--version");

            using var process = Process.Start(startInfo);
            process.Should().NotBeNull();
            var output = process!.StandardOutput.ReadToEnd();
            process.WaitForExit();

            process.ExitCode.Should().Be(0);
            output.Trim().Should().Be("v26.8.1");
        }

        [Fact]
        public void Runtime_ShouldNotRequireNodeModules()
        {
            File.Exists(Path.Combine(GetResourcesPath(), "markdownlint-cli2.mjs")).Should().BeTrue();
            Directory.Exists(Path.Combine(GetResourcesPath(), "node_modules")).Should().BeFalse();
        }

        [Fact]
        public void LintContent_ShouldAddExactlyOneTrailingNewline_WhenMD047Enabled()
        {
            var service = new MarkdownLintCliService();

            var result = service.LintContent("# Test\n\nSome content");

            result.Success.Should().BeTrue();
            result.Content.Should().EndWith("content\n");
            result.Content.Should().NotEndWith("content\n\n");
        }

        [Fact]
        public void LintContent_ShouldWrapBareUrls_WhenMD034Enabled()
        {
            var service = new MarkdownLintCliService();
            var contentWithBareUrl = "# Test\n\nCheck out https://example.com for more info.\n";

            var result = service.LintContent(contentWithBareUrl);

            result.Success.Should().BeTrue();
            result.Content.Should().Contain("<https://example.com>");
            result.Content.Should().NotContain(" https://example.com ");
        }

        [Fact]
        public void LintContent_ShouldReturnFailureAndOriginalContent_WhenServiceUnavailable()
        {
            var resourcesPath = CreateTempDirectory();
            try
            {
                var service = new MarkdownLintCliService(resourcesPath);
                var original = "# Test\n\nContent\n";

                var result = service.LintContent(original);

                service.IsAvailable.Should().BeFalse();
                result.Success.Should().BeFalse();
                result.Content.Should().Be(original);
                result.ErrorMessage.Should().Contain("node.exe not found");
            }
            finally
            {
                Directory.Delete(resourcesPath, true);
            }
        }

        [Fact]
        public void LintContent_ShouldHandleEmptyContent()
        {
            var service = new MarkdownLintCliService();

            var result = service.LintContent("");

            result.Success.Should().BeTrue();
            result.Content.Should().BeEmpty();
        }

        [Fact]
        public void LintContent_ShouldHonorCustomJsonConfiguration()
        {
            var service = new MarkdownLintCliService();
            var directory = CreateTempDirectory("markdownlint config ");
            var configPath = Path.Combine(directory, "custom.markdownlint.json");
            File.WriteAllText(configPath, "{\"default\":true,\"MD034\":false,\"MD047\":false}");
            var markdown = "# Test\n\nVisit https://example.com";

            try
            {
                var result = service.LintContent(markdown, configPath);

                result.Success.Should().BeTrue();
                result.Content.Should().Be(markdown);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void LintContent_ShouldIgnoreNonDescriptiveLinkTextByDefault()
        {
            var service = new MarkdownLintCliService();
            var markdown = "# Test\n\nRead more [here](https://example.com).\n";

            var result = service.LintContent(markdown);

            result.Success.Should().BeTrue();
            result.Content.Should().Contain("[here](https://example.com)");
            result.WarningMessage.Should().BeEmpty();
        }

        [Fact]
        public void LintContent_ShouldAllowCustomConfigurationToEnableMD059()
        {
            var service = new MarkdownLintCliService();
            var directory = CreateTempDirectory();
            var configPath = Path.Combine(directory, "enable-md059.markdownlint.json");
            File.WriteAllText(configPath, "{\"default\":false,\"MD059\":true}");

            try
            {
                var result = service.LintContent("# Test\n\nRead more [here](https://example.com).\n", configPath);

                result.Success.Should().BeTrue();
                result.WarningMessage.Should().Contain("MD059");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void LintContent_ShouldRejectMalformedJsonConfiguration()
        {
            var service = new MarkdownLintCliService();
            var directory = CreateTempDirectory();
            var configPath = Path.Combine(directory, "invalid.markdownlint.json");
            File.WriteAllText(configPath, "{ invalid json }");
            var markdown = "# Test";

            try
            {
                var result = service.LintContent(markdown, configPath);

                result.Success.Should().BeFalse();
                result.Content.Should().Be(markdown);
                result.ErrorMessage.Should().Contain("Invalid Markdown lint configuration");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void LintContent_ShouldKeepFixesAndWarn_WhenIssuesRemain()
        {
            var service = new MarkdownLintCliService();
            var directory = CreateTempDirectory();
            var configPath = Path.Combine(directory, "remaining.markdownlint.json");
            File.WriteAllText(configPath, "{\"default\":false,\"MD013\":true,\"MD047\":true}");
            var markdown = "# Test\n\nThis line is intentionally much longer than eighty characters so MD013 remains after MD047 is automatically fixed by markdownlint-cli2.";

            try
            {
                var result = service.LintContent(markdown, configPath);

                result.Success.Should().BeTrue();
                result.Content.Should().EndWith("\n");
                result.WarningMessage.Should().Contain("MD013");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public async Task LintFileAsync_ShouldHandleSpecialPathsAndPreserveUnicodeAndCrLf()
        {
            var service = new MarkdownLintCliService();
            var directory = CreateTempDirectory("markdownlint path with spaces ");
            var filePath = Path.Combine(directory, "file [1] # test.md");
            var markdown = "# Tést 🚀\r\n\r\nVisit https://example.com";
            File.WriteAllText(filePath, markdown, new UTF8Encoding(false));

            try
            {
                var result = await service.LintFileAsync(filePath);
                var content = File.ReadAllText(filePath);

                result.Success.Should().BeTrue();
                content.Should().Contain("# Tést 🚀");
                content.Should().Contain("<https://example.com>");
                content.Should().EndWith("\r\n");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static string GetResourcesPath()
        {
            return Path.Combine(AppContext.BaseDirectory, "resources");
        }

        private static string CreateTempDirectory(string prefix = "markdownlint_")
        {
            var path = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
