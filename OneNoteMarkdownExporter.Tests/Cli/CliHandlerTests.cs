using FluentAssertions;
using OneNoteMarkdownExporter.Services;
using Xunit;

namespace OneNoteMarkdownExporter.Tests.Cli;

/// <summary>
/// Tests for CLI argument parsing and detection.
/// ShouldRunCli returns true when ANY argument starts with a known CLI flag.
/// </summary>
public class CliHandlerTests
{
    #region ShouldRunCli Tests - No CLI Mode

    [Fact]
    public void ShouldRunCli_WithNoArgs_ReturnsFalse()
    {
        // Arrange
        var args = Array.Empty<string>();

        // Act
        var result = CliHandler.ShouldRunCli(args);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldRunCli_WithUnknownCommand_ReturnsFalse()
    {
        // Arrange - "export" alone is not a flag (doesn't start with --)
        var args = new[] { "export" };

        // Act
        var result = CliHandler.ShouldRunCli(args);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldRunCli_WithListCommand_ReturnsFalse()
    {
        // Arrange - "list" alone is not a flag
        var args = new[] { "list" };

        // Act
        var result = CliHandler.ShouldRunCli(args);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region ShouldRunCli Tests - CLI Mode Activated

    [Fact]
    public void ShouldRunCli_WithHelpArg_ReturnsTrue()
    {
        // Arrange
        var args = new[] { "--help" };

        // Act
        var result = CliHandler.ShouldRunCli(args);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldRunCli_WithShortHelpArg_ReturnsTrue()
    {
        // Arrange
        var args = new[] { "-h" };

        // Act
        var result = CliHandler.ShouldRunCli(args);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldRunCli_WithQuestionMarkHelp_ReturnsTrue()
    {
        // Arrange
        var args = new[] { "-?" };

        // Act
        var result = CliHandler.ShouldRunCli(args);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldRunCli_WithVersionArg_ReturnsTrue()
    {
        // Arrange
        var args = new[] { "--version" };

        // Act
        var result = CliHandler.ShouldRunCli(args);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldRunCli_WithAllFlag_ReturnsTrue()
    {
        // Arrange
        var args = new[] { "--all" };

        // Act
        var result = CliHandler.ShouldRunCli(args);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldRunCli_WithNotebookFlag_ReturnsTrue()
    {
        // Arrange
        var args = new[] { "--notebook", "MyNotebook" };

        // Act
        var result = CliHandler.ShouldRunCli(args);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldRunCli_WithSectionFlag_ReturnsTrue()
    {
        // Arrange
        var args = new[] { "--section", "MySection" };

        // Act
        var result = CliHandler.ShouldRunCli(args);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldRunCli_WithPageFlag_ReturnsTrue()
    {
        // Arrange
        var args = new[] { "--page", "page-id" };

        // Act
        var result = CliHandler.ShouldRunCli(args);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldRunCli_WithOutputFlag_ReturnsTrue()
    {
        // Arrange
        var args = new[] { "--output", "C:\\Export" };

        // Act
        var result = CliHandler.ShouldRunCli(args);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldRunCli_WithShortOutputFlag_ReturnsTrue()
    {
        // Arrange
        var args = new[] { "-o", "C:\\Export" };

        // Act
        var result = CliHandler.ShouldRunCli(args);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldRunCli_WithDryRunFlag_ReturnsTrue()
    {
        // Arrange
        var args = new[] { "--dry-run" };

        // Act
        var result = CliHandler.ShouldRunCli(args);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldRunCli_WithQuietFlag_ReturnsTrue()
    {
        // Arrange
        var args = new[] { "--quiet" };

        // Act
        var result = CliHandler.ShouldRunCli(args);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldRunCli_WithShortQuietFlag_ReturnsTrue()
    {
        // Arrange
        var args = new[] { "-q" };

        // Act
        var result = CliHandler.ShouldRunCli(args);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldRunCli_WithVerboseFlag_ReturnsTrue()
    {
        // Arrange
        var args = new[] { "--verbose" };

        // Act
        var result = CliHandler.ShouldRunCli(args);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldRunCli_WithShortVerboseFlag_ReturnsTrue()
    {
        // Arrange
        var args = new[] { "-v" };

        // Act
        var result = CliHandler.ShouldRunCli(args);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldRunCli_WithOverwriteFlag_ReturnsTrue()
    {
        // Arrange
        var args = new[] { "--overwrite" };

        // Act
        var result = CliHandler.ShouldRunCli(args);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldRunCli_WithNoLintFlag_ReturnsTrue()
    {
        // Arrange
        var args = new[] { "--no-lint" };

        // Act
        var result = CliHandler.ShouldRunCli(args);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldRunCli_WithLintConfigFlag_ReturnsTrue()
    {
        // Arrange
        var args = new[] { "--lint-config", "path/to/config" };

        // Act
        var result = CliHandler.ShouldRunCli(args);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldRunCli_WithListFlag_ReturnsTrue()
    {
        // Arrange
        var args = new[] { "--list" };

        // Act
        var result = CliHandler.ShouldRunCli(args);

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region Case Sensitivity Tests

    [Fact]
    public void ShouldRunCli_WithUpperCaseFlag_ReturnsTrue()
    {
        // Arrange - flags are case-insensitive
        var args = new[] { "--ALL" };

        // Act
        var result = CliHandler.ShouldRunCli(args);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldRunCli_WithMixedCaseFlag_ReturnsTrue()
    {
        // Arrange
        var args = new[] { "--Help" };

        // Act
        var result = CliHandler.ShouldRunCli(args);

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region Multiple Arguments Tests

    [Fact]
    public void ShouldRunCli_WithMixedArgsContainingFlag_ReturnsTrue()
    {
        // Arrange - if ANY arg starts with a flag, returns true
        var args = new[] { "export", "--all", "--output", "C:\\Export" };

        // Act
        var result = CliHandler.ShouldRunCli(args);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldRunCli_WithOnlyUnknownArgs_ReturnsFalse()
    {
        // Arrange
        var args = new[] { "export", "somevalue" };

        // Act
        var result = CliHandler.ShouldRunCli(args);

        // Assert
        result.Should().BeFalse();
    }

    #endregion
}
