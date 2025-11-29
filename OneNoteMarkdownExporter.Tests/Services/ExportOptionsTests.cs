using FluentAssertions;
using OneNoteMarkdownExporter.Services;
using Xunit;

namespace OneNoteMarkdownExporter.Tests.Services;

/// <summary>
/// Tests for ExportOptions validation and defaults.
/// </summary>
public class ExportOptionsTests
{
    [Fact]
    public void CreateDefault_ReturnsOptionsWithLintingEnabled()
    {
        // Act
        var options = ExportOptions.CreateDefault();

        // Assert
        options.ApplyLinting.Should().BeTrue();
    }

    [Fact]
    public void CreateDefault_ReturnsOptionsWithOverwriteDisabled()
    {
        // Act
        var options = ExportOptions.CreateDefault();

        // Assert
        options.Overwrite.Should().BeFalse();
    }

    [Fact]
    public void GetDefaultOutputPath_ReturnsPathContainingDownloads()
    {
        // Act
        var path = ExportOptions.GetDefaultOutputPath();

        // Assert
        path.Should().Contain("Downloads");
    }

    [Fact]
    public void GetDefaultOutputPath_ReturnsPathContainingOneNoteExport()
    {
        // Act
        var path = ExportOptions.GetDefaultOutputPath();

        // Assert
        path.Should().Contain("OneNoteExport");
    }

    [Fact]
    public void HasSelectionCriteria_WithNoOptions_ReturnsFalse()
    {
        // Arrange
        var options = new ExportOptions();

        // Act & Assert
        options.HasSelectionCriteria().Should().BeFalse();
    }

    [Fact]
    public void HasSelectionCriteria_WithExportAll_ReturnsTrue()
    {
        // Arrange
        var options = new ExportOptions { ExportAll = true };

        // Act & Assert
        options.HasSelectionCriteria().Should().BeTrue();
    }

    [Fact]
    public void HasSelectionCriteria_WithNotebookNames_ReturnsTrue()
    {
        // Arrange
        var options = new ExportOptions
        {
            NotebookNames = new List<string> { "MyNotebook" }
        };

        // Act & Assert
        options.HasSelectionCriteria().Should().BeTrue();
    }

    [Fact]
    public void HasSelectionCriteria_WithSectionPaths_ReturnsTrue()
    {
        // Arrange
        var options = new ExportOptions
        {
            SectionPaths = new List<string> { "Notebook/Section" }
        };

        // Act & Assert
        options.HasSelectionCriteria().Should().BeTrue();
    }

    [Fact]
    public void HasSelectionCriteria_WithPageIds_ReturnsTrue()
    {
        // Arrange
        var options = new ExportOptions
        {
            PageIds = new List<string> { "page-id-123" }
        };

        // Act & Assert
        options.HasSelectionCriteria().Should().BeTrue();
    }

    [Fact]
    public void HasSelectionCriteria_WithEmptyLists_ReturnsFalse()
    {
        // Arrange
        var options = new ExportOptions
        {
            NotebookNames = new List<string>(),
            SectionPaths = new List<string>(),
            PageIds = new List<string>()
        };

        // Act & Assert
        options.HasSelectionCriteria().Should().BeFalse();
    }

    [Fact]
    public void HasSelectionCriteria_WithNullLists_ReturnsFalse()
    {
        // Arrange
        var options = new ExportOptions
        {
            NotebookNames = null,
            SectionPaths = null,
            PageIds = null
        };

        // Act & Assert
        options.HasSelectionCriteria().Should().BeFalse();
    }

    [Fact]
    public void OutputPath_DefaultsToEmptyString()
    {
        // Arrange
        var options = new ExportOptions();

        // Assert
        options.OutputPath.Should().BeEmpty();
    }

    [Fact]
    public void ApplyLinting_DefaultsToTrue()
    {
        // Arrange
        var options = new ExportOptions();

        // Assert
        options.ApplyLinting.Should().BeTrue();
    }

    [Fact]
    public void Overwrite_DefaultsToFalse()
    {
        // Arrange
        var options = new ExportOptions();

        // Assert
        options.Overwrite.Should().BeFalse();
    }

    [Fact]
    public void DryRun_DefaultsToFalse()
    {
        // Arrange
        var options = new ExportOptions();

        // Assert
        options.DryRun.Should().BeFalse();
    }

    [Fact]
    public void Verbose_DefaultsToFalse()
    {
        // Arrange
        var options = new ExportOptions();

        // Assert
        options.Verbose.Should().BeFalse();
    }

    [Fact]
    public void Quiet_DefaultsToFalse()
    {
        // Arrange
        var options = new ExportOptions();

        // Assert
        options.Quiet.Should().BeFalse();
    }

    [Fact]
    public void ExportAll_DefaultsToFalse()
    {
        // Arrange
        var options = new ExportOptions();

        // Assert
        options.ExportAll.Should().BeFalse();
    }

    [Fact]
    public void LintConfigPath_DefaultsToNull()
    {
        // Arrange
        var options = new ExportOptions();

        // Assert
        options.LintConfigPath.Should().BeNull();
    }

    [Fact]
    public void HasSelectionCriteria_WithMultipleCriteria_ReturnsTrue()
    {
        // Arrange
        var options = new ExportOptions
        {
            ExportAll = true,
            NotebookNames = new List<string> { "Notebook1" },
            SectionPaths = new List<string> { "Notebook1/Section1" }
        };

        // Act & Assert
        options.HasSelectionCriteria().Should().BeTrue();
    }
}
