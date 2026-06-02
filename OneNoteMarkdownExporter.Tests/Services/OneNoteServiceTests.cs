using FluentAssertions;
using OneNoteMarkdownExporter.Models;
using OneNoteMarkdownExporter.Services;
using Xunit;

namespace OneNoteMarkdownExporter.Tests.Services;

public class OneNoteServiceTests
{
    [Fact]
    public void ParseHierarchyXml_UsesPageLevelsToNestSubpages()
    {
        // Arrange
        var xml = """
            <one:Notebooks xmlns:one="http://schemas.microsoft.com/office/onenote/2013/onenote">
              <one:Notebook ID="notebook-id" name="Notebook">
                <one:Section ID="section-id" name="Section">
                  <one:Page ID="parent-id" name="Parent Page" pageLevel="1" />
                  <one:Page ID="child-id" name="Child Page" pageLevel="2" />
                  <one:Page ID="grandchild-id" name="Grandchild Page" pageLevel="3" />
                  <one:Page ID="sibling-id" name="Sibling Page" pageLevel="1" />
                </one:Section>
              </one:Notebook>
            </one:Notebooks>
            """;

        // Act
        var notebooks = OneNoteService.ParseHierarchyXml(xml);

        // Assert
        var section = notebooks.Single().Children.Single();
        section.Children.Select(page => page.Name).Should().Equal("Parent Page", "Sibling Page");

        var parent = section.Children[0];
        parent.Children.Select(page => page.Name).Should().Equal("Child Page");
        parent.Children[0].Children.Select(page => page.Name).Should().Equal("Grandchild Page");
    }

    [Fact]
    public void ParseHierarchyXml_KeepsPagesFlatWhenPageLevelsMatch()
    {
        // Arrange
        var xml = """
            <one:Notebooks xmlns:one="http://schemas.microsoft.com/office/onenote/2013/onenote">
              <one:Notebook ID="notebook-id" name="Notebook">
                <one:Section ID="section-id" name="Section">
                  <one:Page ID="first-id" name="First Page" pageLevel="0" />
                  <one:Page ID="second-id" name="Second Page" pageLevel="0" />
                </one:Section>
              </one:Notebook>
            </one:Notebooks>
            """;

        // Act
        var notebooks = OneNoteService.ParseHierarchyXml(xml);

        // Assert
        var section = notebooks.Single().Children.Single();
        section.Children.Select(page => page.Name).Should().Equal("First Page", "Second Page");
        section.Children.Should().OnlyContain(page => page.Children.Count == 0);
    }

    [Fact]
    public void ParseHierarchyXml_DefaultsMissingOrInvalidPageLevelsToZero()
    {
        // Arrange
        var xml = """
            <one:Notebooks xmlns:one="http://schemas.microsoft.com/office/onenote/2013/onenote">
              <one:Notebook ID="notebook-id" name="Notebook">
                <one:Section ID="section-id" name="Section">
                  <one:Page ID="missing-id" name="Missing Level" />
                  <one:Page ID="invalid-id" name="Invalid Level" pageLevel="not-a-number" />
                </one:Section>
              </one:Notebook>
            </one:Notebooks>
            """;

        // Act
        var notebooks = OneNoteService.ParseHierarchyXml(xml);

        // Assert
        var section = notebooks.Single().Children.Single();
        section.Children.Should().HaveCount(2);
        section.Children.Should().OnlyContain(page => page.PageLevel == 0);
    }

    [Fact]
    public void BuildPageHierarchy_AttachesIndentedPagesToNearestLowerLevelAncestor()
    {
        // Arrange
        var parent = new OneNoteItem { Id = "parent-id", Name = "Parent Page", Type = OneNoteItemType.Page, PageLevel = 0 };
        var child = new OneNoteItem { Id = "child-id", Name = "Child Page", Type = OneNoteItemType.Page, PageLevel = 2 };
        var sibling = new OneNoteItem { Id = "sibling-id", Name = "Sibling Page", Type = OneNoteItemType.Page, PageLevel = 0 };

        // Act
        var pages = OneNoteService.BuildPageHierarchy(new[] { parent, child, sibling });

        // Assert
        pages.Select(page => page.Name).Should().Equal("Parent Page", "Sibling Page");
        parent.Children.Select(page => page.Name).Should().Equal("Child Page");
    }

    [Fact]
    public void BuildPageHierarchy_KeepsFirstIndentedPageAtRoot()
    {
        // Arrange
        var first = new OneNoteItem { Id = "first-id", Name = "First Page", Type = OneNoteItemType.Page, PageLevel = 2 };
        var child = new OneNoteItem { Id = "child-id", Name = "Child Page", Type = OneNoteItemType.Page, PageLevel = 3 };

        // Act
        var pages = OneNoteService.BuildPageHierarchy(new[] { first, child });

        // Assert
        pages.Should().ContainSingle().Which.Name.Should().Be("First Page");
        first.Children.Should().ContainSingle().Which.Name.Should().Be("Child Page");
    }

    [Fact]
    public void ParseHierarchyXml_ReadsLastModifiedTimeAttribute()
    {
        // Arrange
        var xml = """
            <one:Notebooks xmlns:one="http://schemas.microsoft.com/office/onenote/2013/onenote">
              <one:Notebook ID="notebook-id" name="Notebook" lastModifiedTime="2024-01-15T14:30:00Z">
                <one:Section ID="section-id" name="Section" lastModifiedTime="2024-02-20T09:15:00Z">
                  <one:Page ID="page-id" name="Page" lastModifiedTime="2024-03-10T18:45:30.123Z" />
                </one:Section>
              </one:Notebook>
            </one:Notebooks>
            """;

        // Act
        var notebooks = OneNoteService.ParseHierarchyXml(xml);

        // Assert
        var notebook = notebooks.Single();
        notebook.LastModifiedTime.Should().NotBeNull();
        notebook.LastModifiedTime!.Value.UtcDateTime.Should().Be(new DateTime(2024, 1, 15, 14, 30, 0, DateTimeKind.Utc));

        var section = notebook.Children.Single();
        section.LastModifiedTime.Should().NotBeNull();
        section.LastModifiedTime!.Value.UtcDateTime.Should().Be(new DateTime(2024, 2, 20, 9, 15, 0, DateTimeKind.Utc));

        var page = section.Children.Single();
        page.LastModifiedTime.Should().NotBeNull();
        page.LastModifiedTime!.Value.UtcDateTime.Should().Be(new DateTime(2024, 3, 10, 18, 45, 30, 123, DateTimeKind.Utc));
    }

    [Fact]
    public void ParseHierarchyXml_MissingLastModifiedTimeAttribute_LeavesNull()
    {
        // Arrange
        var xml = """
            <one:Notebooks xmlns:one="http://schemas.microsoft.com/office/onenote/2013/onenote">
              <one:Notebook ID="notebook-id" name="Notebook">
                <one:Section ID="section-id" name="Section">
                  <one:Page ID="page-id" name="Page" />
                </one:Section>
              </one:Notebook>
            </one:Notebooks>
            """;

        // Act
        var notebooks = OneNoteService.ParseHierarchyXml(xml);

        // Assert
        notebooks.Single().LastModifiedTime.Should().BeNull();
        notebooks.Single().Children.Single().LastModifiedTime.Should().BeNull();
        notebooks.Single().Children.Single().Children.Single().LastModifiedTime.Should().BeNull();
    }

    [Fact]
    public void ParseHierarchyXml_InvalidLastModifiedTimeValue_LeavesNull()
    {
        // Arrange
        var xml = """
            <one:Notebooks xmlns:one="http://schemas.microsoft.com/office/onenote/2013/onenote">
              <one:Notebook ID="notebook-id" name="Notebook" lastModifiedTime="not-a-date">
                <one:Section ID="section-id" name="Section" lastModifiedTime="2024-02-20T09:15:00Z">
                  <one:Page ID="page-id" name="Page" lastModifiedTime="garbage" />
                </one:Section>
              </one:Notebook>
            </one:Notebooks>
            """;

        // Act
        var notebooks = OneNoteService.ParseHierarchyXml(xml);

        // Assert
        var notebook = notebooks.Single();
        notebook.LastModifiedTime.Should().BeNull();
        notebook.Children.Single().LastModifiedTime.Should().NotBeNull();
        notebook.Children.Single().Children.Single().LastModifiedTime.Should().BeNull();
    }
}
