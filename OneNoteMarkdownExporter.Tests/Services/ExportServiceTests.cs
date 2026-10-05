using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using OneNoteMarkdownExporter.Models;
using OneNoteMarkdownExporter.Services;
using Xunit;

namespace OneNoteMarkdownExporter.Tests.Services;

public class ExportServiceTests
{
    [Theory]
    [InlineData(AssetOrganizationMode.Centralized, @"..\..\_assets", @"_assets")]
    [InlineData(AssetOrganizationMode.Notebook, @"..\_assets_ProjectNotebook", @"Project Notebook\_assets_ProjectNotebook")]
    [InlineData(AssetOrganizationMode.Section, @"_assets_MeetingNotes", @"Project Notebook\Meeting Notes\_assets_MeetingNotes")]
    [InlineData(AssetOrganizationMode.Page, @"_assets_PlanningPage", @"Project Notebook\Meeting Notes\_assets_PlanningPage")]
    public async Task ExportSelectedAsync_WithAssetOrganizationMode_UsesExpectedAssetsFolderAndRelativePath(
        AssetOrganizationMode mode,
        string expectedRelativeAssetsPath,
        string expectedAssetsRelativeFolder)
    {
        var outputPath = CreateTempExportPath();
        var source = new FakeOneNoteExportSource();
        var converter = new RecordingMarkdownConverter();
        var linter = new PassthroughMarkdownLintService();
        var service = new ExportService(source, converter, linter);
        var notebooks = CreateSelectedHierarchy();
        var options = new ExportOptions
        {
            OutputPath = outputPath,
            AssetOrganizationMode = mode,
            ApplyLinting = false,
            Overwrite = true
        };

        try
        {
            var result = await service.ExportSelectedAsync(notebooks, options);

            result.Success.Should().BeTrue();
            result.ExportedPages.Should().Be(1);
            converter.Calls.Should().ContainSingle();
            converter.Calls[0].AssetsFolder.Should().Be(Path.Combine(outputPath, expectedAssetsRelativeFolder));
            converter.Calls[0].RelativeAssetsPath.Should().Be(expectedRelativeAssetsPath.Replace("\\", "/"));
            File.ReadAllText(Path.Combine(outputPath, "Project Notebook", "Meeting Notes", "Planning Page.md"))
                .Should().Be($"asset:{expectedRelativeAssetsPath.Replace("\\", "/")}");
            Directory.Exists(Path.Combine(outputPath, expectedAssetsRelativeFolder)).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
        }
    }

    [Fact]
    public async Task ExportSelectedAsync_WithCentralizedCustomAssetsFolder_UsesCustomFolder()
    {
        var tempRoot = CreateTempExportPath();
        var outputPath = Path.Combine(tempRoot, "export");
        var assetsPath = Path.Combine(tempRoot, "shared-assets");
        var source = new FakeOneNoteExportSource();
        var converter = new RecordingMarkdownConverter();
        var linter = new PassthroughMarkdownLintService();
        var service = new ExportService(source, converter, linter);
        var notebooks = CreateSelectedHierarchy();
        var options = new ExportOptions
        {
            OutputPath = outputPath,
            AssetsFolderPath = assetsPath,
            AssetOrganizationMode = AssetOrganizationMode.Centralized,
            ApplyLinting = false,
            Overwrite = true
        };

        try
        {
            var result = await service.ExportSelectedAsync(notebooks, options);

            result.Success.Should().BeTrue();
            converter.Calls.Should().ContainSingle();
            converter.Calls[0].AssetsFolder.Should().Be(assetsPath);
            converter.Calls[0].RelativeAssetsPath.Should().Be("../../../shared-assets");
            Directory.Exists(assetsPath).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }

    [Theory]
    [InlineData(AssetOrganizationMode.Centralized, @"_assets")]
    [InlineData(AssetOrganizationMode.Notebook, @"Project Notebook\_assets_ProjectNotebook")]
    [InlineData(AssetOrganizationMode.Section, @"Project Notebook\Meeting Notes\_assets_MeetingNotes")]
    [InlineData(AssetOrganizationMode.Page, @"Project Notebook\Meeting Notes\_assets_PlanningPage")]
    public async Task ExportSelectedAsync_WithTextOnlyPage_DoesNotCreateAssetsFolder(AssetOrganizationMode mode, string expectedAssetsRelativeFolder)
    {
        var outputPath = CreateTempExportPath();
        var source = new FakeOneNoteExportSource();
        var converter = new RecordingMarkdownConverter();
        var linter = new PassthroughMarkdownLintService();
        var service = new ExportService(source, converter, linter);
        var notebooks = CreateSelectedHierarchy();
        var options = new ExportOptions
        {
            OutputPath = outputPath,
            AssetOrganizationMode = mode,
            ApplyLinting = false,
            Overwrite = true
        };

        try
        {
            var result = await service.ExportSelectedAsync(notebooks, options);

            result.Success.Should().BeTrue();
            Directory.Exists(Path.Combine(outputPath, expectedAssetsRelativeFolder)).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
        }
    }

    [Fact]
    public async Task ExportSelectedAsync_WhenConverterWritesAsset_CreatesAssetsFolder()
    {
        var outputPath = CreateTempExportPath();
        var source = new FakeOneNoteExportSource();
        var converter = new AssetWritingMarkdownConverter();
        var linter = new PassthroughMarkdownLintService();
        var service = new ExportService(source, converter, linter);
        var notebooks = CreateSelectedHierarchy();
        var options = new ExportOptions
        {
            OutputPath = outputPath,
            AssetOrganizationMode = AssetOrganizationMode.Page,
            ApplyLinting = false,
            Overwrite = true
        };
        var expectedAssetsFolder = Path.Combine(outputPath, "Project Notebook", "Meeting Notes", "_assets_PlanningPage");

        try
        {
            var result = await service.ExportSelectedAsync(notebooks, options);

            result.Success.Should().BeTrue();
            Directory.Exists(expectedAssetsFolder).Should().BeTrue();
            File.Exists(Path.Combine(expectedAssetsFolder, "image_0001.png")).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
        }
    }

    [Fact]
    public async Task ExportSelectedAsync_WithExistingFileAtGeneratedAssetsPath_FailsPageClearly()
    {
        var outputPath = CreateTempExportPath();
        var assetsFilePath = Path.Combine(outputPath, "Project Notebook", "Meeting Notes", "_assets_PlanningPage");
        var source = new FakeOneNoteExportSource();
        var converter = new RecordingMarkdownConverter();
        var linter = new PassthroughMarkdownLintService();
        var service = new ExportService(source, converter, linter);
        var notebooks = CreateSelectedHierarchy();
        var options = new ExportOptions
        {
            OutputPath = outputPath,
            AssetOrganizationMode = AssetOrganizationMode.Page,
            ApplyLinting = false,
            Overwrite = true
        };

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(assetsFilePath)!);
            File.WriteAllText(assetsFilePath, "not a folder");

            var result = await service.ExportSelectedAsync(notebooks, options);

            result.Success.Should().BeFalse();
            result.FailedPages.Should().Be(1);
            result.Failures.Should().ContainSingle()
                .Which.Should().Contain("Assets folder path points to an existing file:");
        }
        finally
        {
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
        }
    }

    [Fact]
    public async Task ExportSelectedAsync_WithExistingFileAtCustomCentralizedAssetsPath_FailsClearly()
    {
        var tempRoot = CreateTempExportPath();
        var outputPath = Path.Combine(tempRoot, "export");
        var assetsFilePath = Path.Combine(tempRoot, "custom-assets");
        var source = new FakeOneNoteExportSource();
        var converter = new RecordingMarkdownConverter();
        var linter = new PassthroughMarkdownLintService();
        var service = new ExportService(source, converter, linter);
        var notebooks = CreateSelectedHierarchy();
        var options = new ExportOptions
        {
            OutputPath = outputPath,
            AssetsFolderPath = assetsFilePath,
            AssetOrganizationMode = AssetOrganizationMode.Centralized,
            ApplyLinting = false,
            Overwrite = true
        };

        try
        {
            Directory.CreateDirectory(tempRoot);
            File.WriteAllText(assetsFilePath, "not a folder");

            var result = await service.ExportSelectedAsync(notebooks, options);

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("Assets folder path points to an existing file:");
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public async Task ExportSelectedAsync_WithPreserveDatesEnabled_AppliesTimestampsAfterWrite()
    {
        var outputPath = CreateTempExportPath();
        var timestampService = new RecordingFileTimestampService();
        var service = new ExportService(new FakeOneNoteExportSource(), new RecordingMarkdownConverter(), new PassthroughMarkdownLintService(), timestampService, new PassthroughYamlFrontMatterService());
        var notebooks = CreateSelectedHierarchy(created: new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero), modified: new DateTimeOffset(2024, 2, 20, 14, 45, 0, TimeSpan.Zero));
        var options = new ExportOptions { OutputPath = outputPath, ApplyLinting = false, Overwrite = true };
        var expectedFilePath = Path.Combine(outputPath, "Project Notebook", "Meeting Notes", "Planning Page.md");

        try
        {
            var result = await service.ExportSelectedAsync(notebooks, options);

            result.Success.Should().BeTrue();
            timestampService.Calls.Should().ContainSingle();
            timestampService.Calls[0].FilePath.Should().Be(expectedFilePath);
            timestampService.Calls[0].Created.Should().Be(notebooks[0].Children[0].Children[0].CreatedTime);
            timestampService.Calls[0].Modified.Should().Be(notebooks[0].Children[0].Children[0].LastModifiedTime);
            File.Exists(expectedFilePath).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
        }
    }

    [Fact]
    public async Task ExportSelectedAsync_WithPreserveDatesDisabled_DoesNotApplyTimestamps()
    {
        var outputPath = CreateTempExportPath();
        var timestampService = new RecordingFileTimestampService();
        var service = new ExportService(new FakeOneNoteExportSource(), new RecordingMarkdownConverter(), new PassthroughMarkdownLintService(), timestampService, new PassthroughYamlFrontMatterService());
        var notebooks = CreateSelectedHierarchy(created: new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero), modified: new DateTimeOffset(2024, 2, 20, 14, 45, 0, TimeSpan.Zero));
        var options = new ExportOptions { OutputPath = outputPath, PreserveDates = false, ApplyLinting = false, Overwrite = true };

        try
        {
            var result = await service.ExportSelectedAsync(notebooks, options);

            result.Success.Should().BeTrue();
            timestampService.Calls.Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
        }
    }

    [Fact]
    public async Task ExportSelectedAsync_WithNoPageDates_DoesNotApplyTimestamps()
    {
        var outputPath = CreateTempExportPath();
        var timestampService = new RecordingFileTimestampService();
        var service = new ExportService(new FakeOneNoteExportSource(), new RecordingMarkdownConverter(), new PassthroughMarkdownLintService(), timestampService, new PassthroughYamlFrontMatterService());
        var notebooks = CreateSelectedHierarchy();
        var options = new ExportOptions { OutputPath = outputPath, ApplyLinting = false, Overwrite = true };

        try
        {
            var result = await service.ExportSelectedAsync(notebooks, options);

            result.Success.Should().BeTrue();
            timestampService.Calls.Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
        }
    }

    [Fact]
    public async Task ExportSelectedAsync_WhenTimestampServiceThrows_AddsWarningAndExportsPage()
    {
        var outputPath = CreateTempExportPath();
        var timestampService = new ThrowingFileTimestampService();
        var service = new ExportService(new FakeOneNoteExportSource(), new RecordingMarkdownConverter(), new PassthroughMarkdownLintService(), timestampService, new PassthroughYamlFrontMatterService());
        var notebooks = CreateSelectedHierarchy(modified: new DateTimeOffset(2024, 2, 20, 14, 45, 0, TimeSpan.Zero));
        var options = new ExportOptions { OutputPath = outputPath, ApplyLinting = false, Overwrite = true };

        try
        {
            var result = await service.ExportSelectedAsync(notebooks, options);

            result.Success.Should().BeTrue();
            result.ExportedPages.Should().Be(1);
            result.FailedPages.Should().Be(0);
            result.Warnings.Should().ContainSingle().Which.Should().Contain("Warning: Could not preserve dates for 'Planning Page': timestamp failed");
        }
        finally
        {
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
        }
    }

    [Fact]
    public async Task ExportSelectedAsync_WithDateMetadataNone_DoesNotAddYaml()
    {
        var outputPath = CreateTempExportPath();
        var yamlService = new RecordingYamlFrontMatterService();
        var service = new ExportService(new FakeOneNoteExportSource(), new RecordingMarkdownConverter(), new PassthroughMarkdownLintService(), new RecordingFileTimestampService(), yamlService);
        var notebooks = CreateSelectedHierarchy(modified: new DateTimeOffset(2024, 2, 20, 14, 45, 0, TimeSpan.Zero));
        var options = new ExportOptions { OutputPath = outputPath, DateMetadataMode = DateMetadataMode.None, ApplyLinting = false, Overwrite = true };

        try
        {
            var result = await service.ExportSelectedAsync(notebooks, options);

            result.Success.Should().BeTrue();
            yamlService.Calls.Should().Be(0);
            File.ReadAllText(Path.Combine(outputPath, "Project Notebook", "Meeting Notes", "Planning Page.md")).Should().NotStartWith("---");
        }
        finally
        {
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
        }
    }

    [Fact]
    public async Task ExportSelectedAsync_WithDateMetadataYaml_AddsYamlBeforeLinting()
    {
        var outputPath = CreateTempExportPath();
        var yamlService = new RecordingYamlFrontMatterService();
        var linter = new RecordingMarkdownLintService();
        var service = new ExportService(new FakeOneNoteExportSource(), new RecordingMarkdownConverter(), linter, new RecordingFileTimestampService(), yamlService);
        var notebooks = CreateSelectedHierarchy(modified: new DateTimeOffset(2024, 2, 20, 14, 45, 0, TimeSpan.Zero));
        var options = new ExportOptions { OutputPath = outputPath, DateMetadataMode = DateMetadataMode.Yaml, ApplyLinting = true, Overwrite = true };

        try
        {
            var result = await service.ExportSelectedAsync(notebooks, options);

            result.Success.Should().BeTrue();
            yamlService.Calls.Should().Be(1);
            linter.Inputs.Should().ContainSingle().Which.Should().StartWith("---\nmetadata: true\n---\n\n");
        }
        finally
        {
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
        }
    }

    [Fact]
    public async Task ExportSelectedAsync_WithCustomLintConfig_ForwardsNormalizedPath()
    {
        var outputPath = CreateTempExportPath();
        var configPath = Path.Combine(outputPath, "custom.markdownlint.json");
        Directory.CreateDirectory(outputPath);
        File.WriteAllText(configPath, "{\"default\":true}");
        var linter = new RecordingMarkdownLintService();
        var service = new ExportService(new FakeOneNoteExportSource(), new RecordingMarkdownConverter(), linter);
        var options = new ExportOptions
        {
            OutputPath = outputPath,
            LintConfigPath = configPath,
            ApplyLinting = true,
            Overwrite = true
        };

        try
        {
            var result = await service.ExportSelectedAsync(CreateSelectedHierarchy(), options);

            result.Success.Should().BeTrue();
            linter.ConfigPaths.Should().ContainSingle().Which.Should().Be(Path.GetFullPath(configPath));
        }
        finally
        {
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
        }
    }

    [Fact]
    public async Task ExportSelectedAsync_WhenLintRuntimeUnavailable_WarnsOnceAndExportsOriginalContent()
    {
        var outputPath = CreateTempExportPath();
        var linter = new StubMarkdownLintService
        {
            IsAvailable = false,
            UnavailableReason = "runtime missing"
        };
        var service = new ExportService(new FakeOneNoteExportSource(), new RecordingMarkdownConverter(), linter);
        var options = new ExportOptions { OutputPath = outputPath, ApplyLinting = true, Overwrite = true };

        try
        {
            var result = await service.ExportSelectedAsync(CreateSelectedHierarchy(), options);
            var markdownPath = Path.Combine(outputPath, "Project Notebook", "Meeting Notes", "Planning Page.md");

            result.Success.Should().BeTrue();
            result.Warnings.Should().ContainSingle().Which.Should().Contain("Markdown linting is unavailable: runtime missing");
            linter.Calls.Should().Be(0);
            File.ReadAllText(markdownPath).Should().StartWith("asset:");
        }
        finally
        {
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
        }
    }

    [Fact]
    public async Task ExportSelectedAsync_WhenLintingFails_WarnsAndExportsOriginalContent()
    {
        var outputPath = CreateTempExportPath();
        var linter = new StubMarkdownLintService
        {
            IsAvailable = true,
            Result = new LintResult { Success = false, Content = "discarded", ErrorMessage = "lint failed" }
        };
        var service = new ExportService(new FakeOneNoteExportSource(), new RecordingMarkdownConverter(), linter);
        var options = new ExportOptions { OutputPath = outputPath, ApplyLinting = true, Overwrite = true };

        try
        {
            var result = await service.ExportSelectedAsync(CreateSelectedHierarchy(), options);
            var markdownPath = Path.Combine(outputPath, "Project Notebook", "Meeting Notes", "Planning Page.md");

            result.Success.Should().BeTrue();
            result.Warnings.Should().ContainSingle().Which.Should().Contain("Markdown linting failed for 'Planning Page': lint failed");
            linter.Calls.Should().Be(1);
            File.ReadAllText(markdownPath).Should().StartWith("asset:");
        }
        finally
        {
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
        }
    }

    [Fact]
    public async Task ExportSelectedAsync_WithSelectedParentPage_ExportsParentAndSubpagesInNestedFolders()
    {
        var outputPath = CreateTempExportPath();
        var service = new ExportService(new FakeOneNoteExportSource(), new RecordingMarkdownConverter(), new PassthroughMarkdownLintService());
        var notebooks = CreateParentSubpageHierarchy(parentSelected: true, childSelected: false);
        var options = new ExportOptions { OutputPath = outputPath, ApplyLinting = false, Overwrite = true };

        try
        {
            var result = await service.ExportSelectedAsync(notebooks, options);

            result.Success.Should().BeTrue();
            File.Exists(Path.Combine(outputPath, "Notebook", "Section", "Parent Page.md")).Should().BeTrue();
            File.Exists(Path.Combine(outputPath, "Notebook", "Section", "Parent Page", "Child Page.md")).Should().BeTrue();
            File.Exists(Path.Combine(outputPath, "Notebook", "Section", "Parent Page", "Child Page", "Grandchild Page.md")).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
        }
    }

    [Fact]
    public async Task ExportSelectedAsync_WithOnlySelectedSubpage_CreatesParentFolderWithoutParentMarkdown()
    {
        var outputPath = CreateTempExportPath();
        var service = new ExportService(new FakeOneNoteExportSource(), new RecordingMarkdownConverter(), new PassthroughMarkdownLintService());
        var notebooks = CreateParentSubpageHierarchy(parentSelected: false, childSelected: true);
        var options = new ExportOptions { OutputPath = outputPath, ApplyLinting = false, Overwrite = true };

        try
        {
            var result = await service.ExportSelectedAsync(notebooks, options);

            result.Success.Should().BeTrue();
            File.Exists(Path.Combine(outputPath, "Notebook", "Section", "Parent Page.md")).Should().BeFalse();
            Directory.Exists(Path.Combine(outputPath, "Notebook", "Section", "Parent Page")).Should().BeTrue();
            File.Exists(Path.Combine(outputPath, "Notebook", "Section", "Parent Page", "Child Page.md")).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
        }
    }

    [Theory]
    [InlineData(AssetOrganizationMode.Centralized, true)]
    [InlineData(AssetOrganizationMode.Notebook, true)]
    [InlineData(AssetOrganizationMode.Section, true)]
    [InlineData(AssetOrganizationMode.Page, true)]
    [InlineData(AssetOrganizationMode.Centralized, false)]
    [InlineData(AssetOrganizationMode.Notebook, false)]
    [InlineData(AssetOrganizationMode.Section, false)]
    [InlineData(AssetOrganizationMode.Page, false)]
    public async Task ExportSelectedAsync_WithLongHierarchy_ExportsEveryPageAndImageWithinBudget(AssetOrganizationMode mode, bool includeContainers)
    {
        var outputPath = CreateTempExportPath();
        var source = new FakeOneNoteExportSource { PageXml = PageWithImagesXml };
        var service = new ExportService(source, new OneNoteXmlToMarkdownConverter(), new PassthroughMarkdownLintService());
        var options = new ExportOptions { OutputPath = outputPath, AssetOrganizationMode = mode, ApplyLinting = false, Overwrite = true };

        try
        {
            var result = await service.ExportSelectedAsync(CreateLongHierarchy(includeContainers), options);

            result.Success.Should().BeTrue(result.Error);
            result.ExportedPages.Should().Be(3);
            result.FailedPages.Should().Be(0);
            source.RequestedPageIds.Should().Equal("parent-page-id", "child-page-id", "grandchild-page-id");
            var markdownFiles = Directory.GetFiles(outputPath, "*.md", SearchOption.AllDirectories);
            markdownFiles.Should().HaveCount(3);
            Directory.GetFiles(outputPath, "*.png", SearchOption.AllDirectories).Should().HaveCount(6);
            markdownFiles.Select(path => Path.GetRelativePath(outputPath, path).Split(Path.DirectorySeparatorChar).Length)
                .Order().Should().Equal(includeContainers ? new[] { 5, 6, 7 } : new[] { 1, 2, 3 });

            foreach (var markdownFile in markdownFiles)
            {
                markdownFile.Length.Should().BeLessOrEqualTo(ExportPathSanitizer.MaxWin32PathLength);
                var images = GetImagePaths(markdownFile);
                images.Should().OnlyHaveUniqueItems();
                File.ReadAllBytes(images[0]).Should().Equal(new byte[] { 1, 2, 3 });
                File.ReadAllBytes(images[1]).Should().Equal(new byte[] { 4, 5, 6 });
                images.Should().OnlyContain(path => path.Length <= ExportPathSanitizer.MaxWin32PathLength);
            }

            var perPageAssets = mode == AssetOrganizationMode.Page || (!includeContainers && mode != AssetOrganizationMode.Centralized);
            var directories = Directory.GetDirectories(outputPath, "*", SearchOption.AllDirectories);
            directories.Should().HaveCount((includeContainers ? 6 : 2) + (perPageAssets ? 3 : 1));
            directories.Should().OnlyContain(path => path.Length <= ExportPathSanitizer.MaxWin32DirectoryPathLength);
        }
        finally
        {
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
        }
    }

    [Fact]
    public async Task ExportSelectedAsync_WithLongNamesAndOverwriteDisabled_KeepsEveryCopy()
    {
        var outputPath = CreateTempExportPath();
        var source = new FakeOneNoteExportSource { PageXml = PageWithImagesXml };
        var service = new ExportService(source, new OneNoteXmlToMarkdownConverter(), new PassthroughMarkdownLintService());
        var hierarchy = CreateLongHierarchy();
        var options = new ExportOptions { OutputPath = outputPath, AssetOrganizationMode = AssetOrganizationMode.Page, ApplyLinting = false, Overwrite = false };

        try
        {
            var first = await service.ExportSelectedAsync(hierarchy, options).WaitAsync(TimeSpan.FromSeconds(30));
            first.Success.Should().BeTrue(first.Error);
            var originals = Directory.GetFiles(outputPath, "*.md", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllText);

            for (var run = 2; run <= 3; run++)
            {
                var result = await service.ExportSelectedAsync(hierarchy, options).WaitAsync(TimeSpan.FromSeconds(30));

                result.Success.Should().BeTrue(result.Error);
                result.ExportedPages.Should().Be(3);
                Directory.GetFiles(outputPath, "*.md", SearchOption.AllDirectories).Should().HaveCount(3 * run);
                Directory.GetFiles(outputPath, "*.png", SearchOption.AllDirectories).Should().HaveCount(6);
            }

            foreach (var original in originals)
            {
                File.ReadAllText(original.Key).Should().Be(original.Value);
            }
        }
        finally
        {
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
        }
    }

    [Fact]
    public async Task ExportSelectedAsync_WithPartialSelection_ReusesFullExportPaths()
    {
        var outputPath = CreateTempExportPath();
        var source = new FakeOneNoteExportSource { PageXml = PageWithImagesXml };
        var service = new ExportService(source, new OneNoteXmlToMarkdownConverter(), new PassthroughMarkdownLintService());
        var hierarchy = CreateLongHierarchy(includeContainers: false);
        var parent = hierarchy.Single();
        var child = parent.Children.Single();
        var leaf = child.Children.Single();
        var options = new ExportOptions { OutputPath = outputPath, AssetOrganizationMode = AssetOrganizationMode.Page, ApplyLinting = false, Overwrite = true };
        var fullProgress = new RecordingExportProgress();
        var partialProgress = new RecordingExportProgress();

        try
        {
            var full = await service.ExportSelectedAsync(hierarchy, options, fullProgress);
            full.Success.Should().BeTrue(full.Error);
            var fullLeafPath = fullProgress.Updates.Single(update => update.Kind == ExportProgressKind.PageExported && update.Page?.Id == leaf.Id).TargetPath;
            parent.IsSelected = false;
            leaf.IsSelected = true;
            source.RequestedPageIds.Clear();

            var partial = await service.ExportSelectedAsync(hierarchy, options, partialProgress);

            partial.Success.Should().BeTrue(partial.Error);
            partial.ExportedPages.Should().Be(1);
            source.RequestedPageIds.Should().Equal(leaf.Id);
            partialProgress.Updates.Single(update => update.Kind == ExportProgressKind.PageExported).TargetPath.Should().Be(fullLeafPath);
            Directory.GetFiles(outputPath, "*.md", SearchOption.AllDirectories).Should().HaveCount(3);
            Directory.GetFiles(outputPath, "*.png", SearchOption.AllDirectories).Should().HaveCount(6);
        }
        finally
        {
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
        }
    }

    [Fact]
    public async Task ExportSelectedAsync_WithImpossibleLaterHierarchy_StopsBeforeWritingAnyPages()
    {
        var tempRoot = CreateTempExportPath();
        var outputPath = Path.Combine(tempRoot, new string('O', 235 - tempRoot.Length - 1));
        var source = new FakeOneNoteExportSource();
        var service = new ExportService(source, new RecordingMarkdownConverter(), new PassthroughMarkdownLintService());
        var items = CreateLongHierarchy();
        items.Insert(0, new OneNoteItem { Id = "first-page", Name = "First", Type = OneNoteItemType.Page, IsSelected = true });
        var options = new ExportOptions { OutputPath = outputPath, AssetsFolderPath = Path.Combine(tempRoot, "assets"), ApplyLinting = false };

        try
        {
            var result = await service.ExportSelectedAsync(items, options);

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("Choose a shorter output folder");
            result.ExportedPages.Should().Be(0);
            source.RequestedPageIds.Should().BeEmpty();
            Directory.GetFiles(outputPath, "*", SearchOption.AllDirectories).Should().BeEmpty();
            Directory.GetDirectories(outputPath, "*", SearchOption.AllDirectories).Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }

    [Theory]
    [InlineData(247, false, true)]
    [InlineData(248, false, false)]
    [InlineData(248, true, true)]
    public async Task ExportSelectedAsync_WithRootNearFileLimit_ReservesCopiesWhenNeeded(int rootLength, bool overwrite, bool expectedSuccess)
    {
        var tempRoot = CreateTempExportPath();
        var outputPath = Path.Combine(tempRoot, new string('O', rootLength - tempRoot.Length - 1));
        var source = new FakeOneNoteExportSource();
        var service = new ExportService(source, new RecordingMarkdownConverter(), new PassthroughMarkdownLintService());
        var items = new List<OneNoteItem> { new() { Id = "page", Name = "Page", Type = OneNoteItemType.Page, IsSelected = true } };
        var options = new ExportOptions { OutputPath = outputPath, AssetsFolderPath = Path.Combine(tempRoot, "assets"), ApplyLinting = false, Overwrite = overwrite };

        try
        {
            var result = await service.ExportSelectedAsync(items, options);

            result.Success.Should().Be(expectedSuccess, result.Error);
            if (expectedSuccess)
            {
                result.ExportedPages.Should().Be(1);
                var repeated = await service.ExportSelectedAsync(items, options).WaitAsync(TimeSpan.FromSeconds(30));
                repeated.Success.Should().BeTrue(repeated.Error);
                var files = Directory.GetFiles(outputPath, "*.md");
                files.Should().HaveCount(overwrite ? 1 : 2);
                files.Should().OnlyContain(path => path.Length <= ExportPathSanitizer.MaxWin32PathLength);
            }
            else
            {
                result.Error.Should().Contain("Choose a shorter output folder");
                source.RequestedPageIds.Should().BeEmpty();
                Directory.GetFiles(outputPath, "*", SearchOption.AllDirectories).Should().BeEmpty();
            }
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }

    [Theory]
    [InlineData(245)]
    [InlineData(246)]
    public async Task ExportSelectedAsync_WithCustomAssetsAtFileLimit_PreservesBothImages(int assetsPathLength)
    {
        var tempRoot = CreateTempExportPath();
        var outputPath = Path.Combine(tempRoot, "export");
        var assetsPath = Path.Combine(tempRoot, new string('A', assetsPathLength - tempRoot.Length - 1));
        var source = new FakeOneNoteExportSource { PageXml = PageWithImagesXml };
        var service = new ExportService(source, new OneNoteXmlToMarkdownConverter(), new PassthroughMarkdownLintService());
        var options = new ExportOptions { OutputPath = outputPath, AssetsFolderPath = assetsPath, ApplyLinting = false, Overwrite = true };

        try
        {
            var result = await service.ExportSelectedAsync(CreateSelectedHierarchy(), options);

            result.Success.Should().BeTrue(result.Error);
            var images = GetImagePaths(Directory.GetFiles(outputPath, "*.md", SearchOption.AllDirectories).Single());
            images.Should().OnlyHaveUniqueItems();
            images.Should().OnlyContain(path => path.Length == assetsPathLength + 1 + 8 + ".png".Length);
            File.ReadAllBytes(images[0]).Should().Equal(new byte[] { 1, 2, 3 });
            File.ReadAllBytes(images[1]).Should().Equal(new byte[] { 4, 5, 6 });
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public async Task ExportSelectedAsync_WithImpossibleCustomAssetsPath_StopsBeforeReadingPages()
    {
        var tempRoot = CreateTempExportPath();
        var outputPath = Path.Combine(tempRoot, "export");
        var assetsPath = Path.Combine(tempRoot, new string('A', 247 - tempRoot.Length - 1));
        var source = new FakeOneNoteExportSource { PageXml = PageWithImagesXml };
        var service = new ExportService(source, new OneNoteXmlToMarkdownConverter(), new PassthroughMarkdownLintService());
        var options = new ExportOptions { OutputPath = outputPath, AssetsFolderPath = assetsPath, ApplyLinting = false };

        try
        {
            var result = await service.ExportSelectedAsync(CreateSelectedHierarchy(), options);

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("Choose a shorter output or assets folder");
            source.RequestedPageIds.Should().BeEmpty();
            Directory.GetFiles(outputPath, "*", SearchOption.AllDirectories).Should().BeEmpty();
            Directory.Exists(assetsPath).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }

    private const string PageWithImagesXml = """
        <one:Page xmlns:one="http://schemas.microsoft.com/office/onenote/2013/onenote">
            <one:Image format="png"><one:Data>AQID</one:Data></one:Image>
            <one:Image format="png"><one:Data>BAUG</one:Data></one:Image>
        </one:Page>
        """;

    private static string[] GetImagePaths(string markdownFile)
    {
        var markdown = File.ReadAllText(markdownFile);
        markdown.Should().NotContain("Image export failed");
        var matches = Regex.Matches(markdown, @"!\[image\]\(([^)]+)\)");
        matches.Should().HaveCount(2);
        return matches.Select(match => Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(markdownFile)!,
            Uri.UnescapeDataString(match.Groups[1].Value).Replace('/', Path.DirectorySeparatorChar)))).ToArray();
    }

    private static List<OneNoteItem> CreateLongHierarchy(bool includeContainers = true)
    {
        var hierarchy = CreateParentSubpageHierarchy(parentSelected: true, childSelected: false);
        var notebook = hierarchy.Single();
        var section = notebook.Children.Single();
        var parent = section.Children.Single();
        var child = parent.Children.Single();
        var leaf = child.Children.Single();
        notebook.Name = new string('N', 300);
        section.Name = new string('S', 300);
        parent.Name = new string('P', 300);
        child.Name = new string('C', 300);
        leaf.Name = new string('L', 300);
        var innerGroup = new OneNoteItem { Id = "inner-group", Name = new string('I', 300), Type = OneNoteItemType.SectionGroup, Children = { section } };
        var outerGroup = new OneNoteItem { Id = "outer-group", Name = new string('O', 300), Type = OneNoteItemType.SectionGroup, Children = { innerGroup } };
        notebook.Children.Clear();
        notebook.Children.Add(outerGroup);
        return includeContainers ? hierarchy : new List<OneNoteItem> { parent };
    }

    private static List<OneNoteItem> CreateSelectedHierarchy(DateTimeOffset? created = null, DateTimeOffset? modified = null)
    {
        return new List<OneNoteItem>
        {
            new OneNoteItem
            {
                Id = "notebook-id",
                Name = "Project Notebook",
                Type = OneNoteItemType.Notebook,
                IsSelected = true,
                Children =
                {
                    new OneNoteItem
                    {
                        Id = "section-id",
                        Name = "Meeting Notes",
                        Type = OneNoteItemType.Section,
                        Children =
                        {
                            new OneNoteItem
                            {
                                Id = "page-id",
                                Name = "Planning Page",
                                Type = OneNoteItemType.Page,
                                CreatedTime = created,
                                LastModifiedTime = modified
                            }
                        }
                    }
                }
            }
        };
    }

    private static List<OneNoteItem> CreateParentSubpageHierarchy(bool parentSelected, bool childSelected)
    {
        return new List<OneNoteItem>
        {
            new OneNoteItem
            {
                Id = "notebook-id",
                Name = "Notebook",
                Type = OneNoteItemType.Notebook,
                Children =
                {
                    new OneNoteItem
                    {
                        Id = "section-id",
                        Name = "Section",
                        Type = OneNoteItemType.Section,
                        Children =
                        {
                            new OneNoteItem
                            {
                                Id = "parent-page-id",
                                Name = "Parent Page",
                                Type = OneNoteItemType.Page,
                                IsSelected = parentSelected,
                                Children =
                                {
                                    new OneNoteItem
                                    {
                                        Id = "child-page-id",
                                        Name = "Child Page",
                                        Type = OneNoteItemType.Page,
                                        IsSelected = childSelected,
                                        Children =
                                        {
                                            new OneNoteItem
                                            {
                                                Id = "grandchild-page-id",
                                                Name = "Grandchild Page",
                                                Type = OneNoteItemType.Page
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };
    }

    private static string CreateTempExportPath()
    {
        return Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    }

    private sealed class FakeOneNoteExportSource : IOneNoteExportSource
    {
        public string PageXml { get; init; } = "<one:Page xmlns:one=\"http://schemas.microsoft.com/office/onenote/2013/onenote\" />";
        public List<string> RequestedPageIds { get; } = new();

        public List<OneNoteItem> GetNotebookHierarchy()
        {
            return CreateSelectedHierarchy();
        }

        public string GetPageContent(string pageId)
        {
            RequestedPageIds.Add(pageId);
            return PageXml;
        }

        public string? GetBinaryPageContent(string pageId, string callbackId)
        {
            return null;
        }
    }

    private sealed class RecordingExportProgress : IProgress<ExportProgressUpdate>
    {
        public List<ExportProgressUpdate> Updates { get; } = new();

        public void Report(ExportProgressUpdate update)
        {
            Updates.Add(update);
        }
    }

    private sealed class RecordingMarkdownConverter : IMarkdownContentConverter
    {
        public List<ConverterCall> Calls { get; } = new();

        public string Convert(string pageXml, string assetsFolder, string relativeAssetsPath, BinaryContentFetcher? binaryContentFetcher = null, string? pagePrefix = null)
        {
            Calls.Add(new ConverterCall(assetsFolder, relativeAssetsPath));
            return $"asset:{relativeAssetsPath}";
        }
    }

    private sealed class AssetWritingMarkdownConverter : IMarkdownContentConverter
    {
        public string Convert(string pageXml, string assetsFolder, string relativeAssetsPath, BinaryContentFetcher? binaryContentFetcher = null, string? pagePrefix = null)
        {
            Directory.CreateDirectory(assetsFolder);
            File.WriteAllBytes(Path.Combine(assetsFolder, "image_0001.png"), new byte[] { 1, 2, 3 });
            return $"![image]({relativeAssetsPath}/image_0001.png)";
        }
    }

    private sealed record ConverterCall(string AssetsFolder, string RelativeAssetsPath);

    private sealed class PassthroughMarkdownLintService : IMarkdownLintService
    {
        public bool IsAvailable => true;
        public string UnavailableReason => string.Empty;

        public LintResult LintContent(string markdown, string? configPath = null)
        {
            return new LintResult { Success = true, Content = markdown };
        }
    }

    private sealed class RecordingMarkdownLintService : IMarkdownLintService
    {
        public bool IsAvailable => true;
        public string UnavailableReason => string.Empty;
        public List<string> Inputs { get; } = new();
        public List<string?> ConfigPaths { get; } = new();

        public LintResult LintContent(string markdown, string? configPath = null)
        {
            Inputs.Add(markdown);
            ConfigPaths.Add(configPath);
            return new LintResult { Success = true, Content = markdown };
        }
    }

    private sealed class StubMarkdownLintService : IMarkdownLintService
    {
        public bool IsAvailable { get; init; }
        public string UnavailableReason { get; init; } = string.Empty;
        public LintResult Result { get; init; } = new() { Success = true };
        public int Calls { get; private set; }

        public LintResult LintContent(string markdown, string? configPath = null)
        {
            Calls++;
            return Result;
        }
    }

    private sealed class RecordingFileTimestampService : IFileTimestampService
    {
        public List<TimestampCall> Calls { get; } = new();

        public void ApplyTimestamps(string filePath, DateTimeOffset? created, DateTimeOffset? modified)
        {
            Calls.Add(new TimestampCall(filePath, created, modified));
        }
    }

    private sealed class ThrowingFileTimestampService : IFileTimestampService
    {
        public void ApplyTimestamps(string filePath, DateTimeOffset? created, DateTimeOffset? modified)
        {
            throw new IOException("timestamp failed");
        }
    }

    private sealed class PassthroughYamlFrontMatterService : IYamlFrontMatterService
    {
        public string AddFrontMatter(string markdown, OneNoteItem page)
        {
            return markdown;
        }
    }

    private sealed class RecordingYamlFrontMatterService : IYamlFrontMatterService
    {
        public int Calls { get; private set; }

        public string AddFrontMatter(string markdown, OneNoteItem page)
        {
            Calls++;
            return $"---\nmetadata: true\n---\n\n{markdown}";
        }
    }

    private sealed record TimestampCall(string FilePath, DateTimeOffset? Created, DateTimeOffset? Modified);
}
