using System.IO;
using FluentAssertions;
using OneNoteMarkdownExporter.Models;
using OneNoteMarkdownExporter.Services;
using Xunit;

namespace OneNoteMarkdownExporter.Tests.Services;

public class ExportPathPlannerTests
{
    [Fact]
    public void GetCentralizedAssetsFolderPath_WithDefaultOptions_UsesOutputAssetsFolder()
    {
        var outputPath = CreateTempExportPath();
        var planner = new ExportPathPlanner(outputPath, new ExportOptions());

        var result = planner.GetCentralizedAssetsFolderPath();

        result.Should().Be(Path.Combine(outputPath, "_assets"));
    }

    [Fact]
    public void GetCentralizedAssetsFolderPath_WithRelativeCustomPath_ResolvesFromOutputRoot()
    {
        var outputPath = CreateTempExportPath();
        var planner = new ExportPathPlanner(outputPath, new ExportOptions { AssetsFolderPath = @"media\images" });

        var result = planner.GetCentralizedAssetsFolderPath();

        result.Should().Be(Path.Combine(outputPath, "media", "images"));
    }

    [Fact]
    public void GetScopedAssetsFolderPath_ForNotebook_UsesNotebookFolderAndName()
    {
        var outputPath = CreateTempExportPath();
        var notebook = new OneNoteItem { Id = "notebook-id", Name = "Project Notes", Type = OneNoteItemType.Notebook };
        var planner = new ExportPathPlanner(outputPath, new ExportOptions { AssetOrganizationMode = AssetOrganizationMode.Notebook });
        var notebookFolder = planner.GetContainerFolderPath(outputPath, notebook);

        var result = planner.GetScopedAssetsFolderPath(notebookFolder, notebook);

        result.Should().Be(Path.Combine(notebookFolder, "_assets_ProjectNotes"));
    }

    [Fact]
    public void GetScopedAssetsFolderPath_ForSection_UsesSectionFolderAndName()
    {
        var outputPath = CreateTempExportPath();
        var section = new OneNoteItem { Id = "section-id", Name = "Meeting Notes", Type = OneNoteItemType.Section };
        var planner = new ExportPathPlanner(outputPath, new ExportOptions { AssetOrganizationMode = AssetOrganizationMode.Section });
        var sectionFolder = Path.Combine(outputPath, "Notebook", "Meeting Notes");

        var result = planner.GetScopedAssetsFolderPath(sectionFolder, section);

        result.Should().Be(Path.Combine(sectionFolder, "_assets_MeetingNotes"));
    }

    [Fact]
    public void CreatePageContext_ForPageAssets_UsesPageFolderAndRelativePath()
    {
        var outputPath = CreateTempExportPath();
        var page = new OneNoteItem { Id = "page-id", Name = "Page Name", Type = OneNoteItemType.Page };
        var planner = new ExportPathPlanner(outputPath, new ExportOptions { AssetOrganizationMode = AssetOrganizationMode.Page });
        var sectionFolder = Path.Combine(outputPath, "Notebook", "Section");
        var assetsFolder = planner.GetScopedAssetsFolderPath(sectionFolder, page);

        var result = planner.CreatePageContext(page, sectionFolder, assetsFolder);

        result.MarkdownFilePath.Should().Be(Path.Combine(sectionFolder, "Page Name.md"));
        result.MarkdownFolderPath.Should().Be(sectionFolder);
        result.AssetsFolderPath.Should().Be(Path.Combine(sectionFolder, "_assets_PageName"));
        result.RelativeAssetsPath.Should().Be("_assets_PageName");
    }

    [Fact]
    public void CreatePageContext_ForNestedSubpageAssets_UsesSubpageFolderAndRelativePath()
    {
        var outputPath = CreateTempExportPath();
        var subpage = new OneNoteItem { Id = "subpage-id", Name = "Sub Page", Type = OneNoteItemType.Page };
        var planner = new ExportPathPlanner(outputPath, new ExportOptions { AssetOrganizationMode = AssetOrganizationMode.Page });
        var subpageFolder = Path.Combine(outputPath, "Notebook", "Section", "Parent Page");
        var assetsFolder = planner.GetScopedAssetsFolderPath(subpageFolder, subpage);

        var result = planner.CreatePageContext(subpage, subpageFolder, assetsFolder);

        result.MarkdownFilePath.Should().Be(Path.Combine(subpageFolder, "Sub Page.md"));
        result.AssetsFolderPath.Should().Be(Path.Combine(subpageFolder, "_assets_SubPage"));
        result.RelativeAssetsPath.Should().Be("_assets_SubPage");
    }

    [Fact]
    public void GetScopedAssetsFolderPath_WithSameLocationCollision_AddsHashOnlyToSecondFolder()
    {
        var outputPath = CreateTempExportPath();
        var firstPage = new OneNoteItem { Id = "first-page-id", Name = "Project Notes", Type = OneNoteItemType.Page };
        var secondPage = new OneNoteItem { Id = "second-page-id", Name = "Project Notes", Type = OneNoteItemType.Page };
        var planner = new ExportPathPlanner(outputPath, new ExportOptions { AssetOrganizationMode = AssetOrganizationMode.Page });
        var sectionFolder = Path.Combine(outputPath, "Notebook", "Section");

        var first = planner.GetScopedAssetsFolderPath(sectionFolder, firstPage);
        var second = planner.GetScopedAssetsFolderPath(sectionFolder, secondPage);

        Path.GetFileName(first).Should().Be("_assets_ProjectNotes");
        Path.GetFileName(second).Should().MatchRegex("^_assets_ProjectNotes_[0-9a-f]{8}$");
    }

    [Theory]
    [InlineData(243)]
    [InlineData(244)]
    [InlineData(245)]
    public void CreatePageContext_WithLeafNearDirectoryLimit_DoesNotPlanAnUnusedFolder(int parentLength)
    {
        var planner = new ExportPathPlanner(CreateTempExportPath(), new ExportOptions());
        var page = new OneNoteItem { Id = "leaf-id", Name = "Leaf Page", Type = OneNoteItemType.Page };
        var parent = CreateFolderPathOfLength(parentLength);

        var context = planner.CreatePageContext(page, parent, planner.GetCentralizedAssetsFolderPath());

        context.MarkdownFilePath.Should().Be(Path.Combine(parent, "Leaf Page.md"));
        context.MarkdownFilePath.Length.Should().BeLessOrEqualTo(ExportPathSanitizer.MaxWin32PathLength);
    }

    [Theory]
    [InlineData(AssetOrganizationMode.Centralized, 9)]
    [InlineData(AssetOrganizationMode.Page, 18)]
    public void GetChildPageFolderPath_WithLongDescendants_ReservesFoldersAndAssets(AssetOrganizationMode mode, int expectedReserve)
    {
        var leaf = new OneNoteItem { Id = "leaf", Name = new string('L', 300), Type = OneNoteItemType.Page };
        var child = new OneNoteItem { Id = "child", Name = new string('C', 300), Type = OneNoteItemType.Page, Children = { leaf } };
        var parent = new OneNoteItem { Id = "parent", Name = new string('P', 300), Type = OneNoteItemType.Page, IsSelected = true, Children = { child } };
        var planner = new ExportPathPlanner(CreateTempExportPath(), new ExportOptions { AssetOrganizationMode = mode });
        planner.ValidatePaths(new[] { parent });

        var parentFolder = planner.GetChildPageFolderPath(planner.OutputRoot, parent);
        var childFolder = planner.GetChildPageFolderPath(parentFolder, child);
        var assetsFolder = mode == AssetOrganizationMode.Centralized
            ? planner.GetCentralizedAssetsFolderPath()
            : planner.GetScopedAssetsFolderPath(childFolder, leaf);
        var context = planner.CreatePageContext(leaf, childFolder, assetsFolder);

        parentFolder.Length.Should().Be(ExportPathSanitizer.MaxWin32DirectoryPathLength - expectedReserve);
        childFolder.Length.Should().BeLessOrEqualTo(ExportPathSanitizer.MaxWin32DirectoryPathLength);
        context.MarkdownFilePath.Length.Should().BeLessOrEqualTo(ExportPathSanitizer.MaxWin32PathLength);
        Path.Combine(assetsFolder, ExportPathSanitizer.GetSafeAssetFileName(assetsFolder, leaf.Name, 1, ".png"))
            .Length.Should().BeLessOrEqualTo(ExportPathSanitizer.MaxWin32PathLength);
    }

    [Fact]
    public void ValidatePaths_WithDeepShortNames_UsesTheirActualLengths()
    {
        var root = new OneNoteItem { Name = "A", Type = OneNoteItemType.SectionGroup, IsSelected = true };
        var current = root;
        for (var i = 1; i < 10; i++)
        {
            var child = new OneNoteItem { Name = "A", Type = OneNoteItemType.SectionGroup };
            current.Children.Add(child);
            current = child;
        }

        var page = new OneNoteItem { Id = "page", Name = new string('P', 300), Type = OneNoteItemType.Page };
        current.Children.Add(page);
        var planner = new ExportPathPlanner(CreateFolderPathOfLength(221), new ExportOptions());
        planner.ValidatePaths(new[] { root });
        var folder = planner.OutputRoot;

        for (var item = root; item.Type != OneNoteItemType.Page; item = item.Children.Single())
        {
            folder = planner.GetContainerFolderPath(folder, item);
            Path.GetFileName(folder).Should().Be("A");
        }

        folder.Length.Should().Be(241);
        planner.CreatePageContext(page, folder, planner.GetCentralizedAssetsFolderPath())
            .MarkdownFilePath.Length.Should().Be(ExportPathSanitizer.MaxWin32PathLength);
    }

    [Fact]
    public void ValidatePaths_WithImpossibleHierarchy_RequestsShorterRoot()
    {
        var page = new OneNoteItem { Id = "page", Name = "Page", Type = OneNoteItemType.Page };
        var section = new OneNoteItem { Id = "section", Name = new string('S', 300), Type = OneNoteItemType.Section, Children = { page } };
        var notebook = new OneNoteItem { Id = "notebook", Name = new string('N', 300), Type = OneNoteItemType.Notebook, IsSelected = true, Children = { section } };
        var planner = new ExportPathPlanner(CreateFolderPathOfLength(240), new ExportOptions());

        var act = () => planner.ValidatePaths(new[] { notebook });

        act.Should().Throw<PathTooLongException>().WithMessage("*even with compact names*Choose a shorter output folder*");
    }

    private static string CreateFolderPathOfLength(int length)
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        return Path.Combine(root, new string('P', length - root.Length));
    }

    private static string CreateTempExportPath()
    {
        return Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    }
}
