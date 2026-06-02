using System.IO;
using FluentAssertions;
using OneNoteMarkdownExporter.Services;
using Xunit;

namespace OneNoteMarkdownExporter.Tests.Converters;

/// <summary>
/// Tests for the OneNoteXmlToMarkdownConverter - the core conversion engine.
/// </summary>
public class OneNoteXmlToMarkdownConverterTests
{
    private readonly OneNoteXmlToMarkdownConverter _converter;

    public OneNoteXmlToMarkdownConverterTests()
    {
        _converter = new OneNoteXmlToMarkdownConverter();
    }

    #region Basic Conversion Tests

    [Fact]
    public void Convert_SimpleText_ReturnsMarkdown()
    {
        // Arrange
        var xml = CreatePageXml("<one:T><![CDATA[Hello World]]></one:T>");

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "test");

        // Assert
        result.Should().Contain("Hello World");
    }

    [Fact]
    public void Convert_PageWithTitle_IncludesH1Heading()
    {
        // Arrange
        var xml = @"<?xml version=""1.0""?>
            <one:Page xmlns:one=""http://schemas.microsoft.com/office/onenote/2013/onenote"">
                <one:Title>
                    <one:OE><one:T><![CDATA[My Page Title]]></one:T></one:OE>
                </one:Title>
                <one:Outline>
                    <one:OEChildren>
                        <one:OE><one:T><![CDATA[Content]]></one:T></one:OE>
                    </one:OEChildren>
                </one:Outline>
            </one:Page>";

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "test");

        // Assert
        result.Should().Contain("# My Page Title");
    }

    [Fact]
    public void Convert_EmptyPage_ReturnsNonNull()
    {
        // Arrange
        var xml = @"<?xml version=""1.0""?>
            <one:Page xmlns:one=""http://schemas.microsoft.com/office/onenote/2013/onenote"">
            </one:Page>";

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "test");

        // Assert
        result.Should().NotBeNull();
    }

    #endregion

    #region Text Formatting Tests

    [Fact]
    public void Convert_BoldText_ConvertsToStrong()
    {
        // Arrange - Use single quotes for style attribute (OneNote format)
        var xml = CreatePageXml("<one:T><![CDATA[<span style='font-weight:bold'>Bold Text</span>]]></one:T>");

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "test");

        // Assert
        result.Should().Contain("**Bold Text**");
    }

    [Fact]
    public void Convert_ItalicText_ConvertsToEmphasis()
    {
        // Arrange - Use single quotes for style attribute (OneNote format)
        var xml = CreatePageXml("<one:T><![CDATA[<span style='font-style:italic'>Italic Text</span>]]></one:T>");

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "test");

        // Assert
        result.Should().Contain("*Italic Text*");
    }

    [Fact]
    public void Convert_StrikethroughText_ConvertsToDelTag()
    {
        // Arrange - Strikethrough uses T element style attribute, not span
        var xml = CreatePageXmlWithStyle("Deleted", "text-decoration:line-through");

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "test");

        // Assert
        result.Should().Contain("~~Deleted~~");
    }

    [Fact]
    public void Convert_HighlightedText_ConvertsToBold()
    {
        // Arrange - highlighted text has background color (single quotes)
        var xml = CreatePageXml("<one:T><![CDATA[<span style='background:yellow'>Highlighted</span>]]></one:T>");

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "test");

        // Assert
        result.Should().Contain("**Highlighted**");
    }

    [Fact]
    public void Convert_BoldAndItalic_PreservesBothFormats()
    {
        // Arrange - Use single quotes for style attribute (OneNote format)
        var xml = CreatePageXml("<one:T><![CDATA[<span style='font-weight:bold;font-style:italic'>Bold Italic</span>]]></one:T>");

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "test");

        // Assert
        // Should contain both bold and italic markers
        result.Should().Contain("**");
        result.Should().Contain("*");
    }

    #endregion

    #region List Tests

    [Fact]
    public void Convert_BulletList_CreatesUnorderedList()
    {
        // Arrange
        var xml = @"<?xml version=""1.0""?>
            <one:Page xmlns:one=""http://schemas.microsoft.com/office/onenote/2013/onenote"">
                <one:Outline>
                    <one:OEChildren>
                        <one:OE>
                            <one:List><one:Bullet /></one:List>
                            <one:T><![CDATA[Item 1]]></one:T>
                        </one:OE>
                        <one:OE>
                            <one:List><one:Bullet /></one:List>
                            <one:T><![CDATA[Item 2]]></one:T>
                        </one:OE>
                    </one:OEChildren>
                </one:Outline>
            </one:Page>";

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "test");

        // Assert
        result.Should().Contain("- Item 1");
        result.Should().Contain("- Item 2");
    }

    [Fact]
    public void Convert_NumberedList_CreatesOrderedList()
    {
        // Arrange
        var xml = @"<?xml version=""1.0""?>
            <one:Page xmlns:one=""http://schemas.microsoft.com/office/onenote/2013/onenote"">
                <one:Outline>
                    <one:OEChildren>
                        <one:OE>
                            <one:List><one:Number /></one:List>
                            <one:T><![CDATA[First]]></one:T>
                        </one:OE>
                        <one:OE>
                            <one:List><one:Number /></one:List>
                            <one:T><![CDATA[Second]]></one:T>
                        </one:OE>
                    </one:OEChildren>
                </one:Outline>
            </one:Page>";

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "test");

        // Assert
        result.Should().Contain("1. First");
        result.Should().Contain("2. Second");
    }

    [Fact]
    public void Convert_NestedBulletList_PreservesIndentation()
    {
        // Arrange
        var xml = @"<?xml version=""1.0""?>
            <one:Page xmlns:one=""http://schemas.microsoft.com/office/onenote/2013/onenote"">
                <one:Outline>
                    <one:OEChildren>
                        <one:OE>
                            <one:List><one:Bullet /></one:List>
                            <one:T><![CDATA[Parent]]></one:T>
                            <one:OEChildren>
                                <one:OE>
                                    <one:List><one:Bullet /></one:List>
                                    <one:T><![CDATA[Child]]></one:T>
                                </one:OE>
                            </one:OEChildren>
                        </one:OE>
                    </one:OEChildren>
                </one:Outline>
            </one:Page>";

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "test");

        // Assert
        result.Should().Contain("- Parent");
        result.Should().Contain("Child"); // Should be indented or nested
    }

    #endregion

    #region Link Tests

    [Fact]
    public void Convert_SimpleLink_CreatesMarkdownLink()
    {
        // Arrange
        var xml = CreatePageXml(@"<one:T><![CDATA[<a href=""https://example.com"">Click Here</a>]]></one:T>");

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "test");

        // Assert
        result.Should().Contain("[Click Here](https://example.com)");
    }

    [Fact]
    public void Convert_NakedUrl_ContainsLink()
    {
        // Arrange - when link text matches URL
        var xml = CreatePageXml(@"<one:T><![CDATA[<a href=""https://example.com"">https://example.com</a>]]></one:T>");

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "test");

        // Assert - URL should be preserved in some link format
        result.Should().Contain("https://example.com");
    }

    [Fact]
    public void Convert_LinkWithSpecialChars_PreservesUrl()
    {
        // Arrange
        var xml = CreatePageXml(@"<one:T><![CDATA[<a href=""https://example.com/path?query=value&other=123"">Link</a>]]></one:T>");

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "test");

        // Assert
        result.Should().Contain("https://example.com/path?query=value&other=123");
    }

    #endregion

    #region Table Tests

    [Fact]
    public void Convert_SimpleTable_CreatesMarkdownTable()
    {
        // Arrange
        var xml = @"<?xml version=""1.0""?>
            <one:Page xmlns:one=""http://schemas.microsoft.com/office/onenote/2013/onenote"">
                <one:Outline>
                    <one:OEChildren>
                        <one:OE>
                            <one:Table>
                                <one:Row>
                                    <one:Cell><one:OEChildren><one:OE><one:T><![CDATA[A]]></one:T></one:OE></one:OEChildren></one:Cell>
                                    <one:Cell><one:OEChildren><one:OE><one:T><![CDATA[B]]></one:T></one:OE></one:OEChildren></one:Cell>
                                </one:Row>
                                <one:Row>
                                    <one:Cell><one:OEChildren><one:OE><one:T><![CDATA[1]]></one:T></one:OE></one:OEChildren></one:Cell>
                                    <one:Cell><one:OEChildren><one:OE><one:T><![CDATA[2]]></one:T></one:OE></one:OEChildren></one:Cell>
                                </one:Row>
                            </one:Table>
                        </one:OE>
                    </one:OEChildren>
                </one:Outline>
            </one:Page>";

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "test");

        // Assert - ReverseMarkdown converts tables to Markdown format
        result.Should().Contain("|");
        result.Should().Contain("---");
    }

    #endregion

    #region Image Tests

    [Fact]
    public void Convert_ImageWithCustomRelativeAssetsPath_UsesRelativeAssetsPathInMarkdown()
    {
        // Arrange
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var assetsFolder = Path.Combine(tempRoot, "shared", "assets");
        var xml = @"<?xml version=""1.0""?>
            <one:Page xmlns:one=""http://schemas.microsoft.com/office/onenote/2013/onenote"">
                <one:Image format=""png""><one:Data>AQID</one:Data></one:Image>
            </one:Page>";

        try
        {
            // Act
            var result = _converter.Convert(xml, assetsFolder, "../shared/assets", null, "page");

            // Assert
            result.Should().Contain("../shared/assets/page_image_0001.png");
            File.Exists(Path.Combine(assetsFolder, "page_image_0001.png")).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public void Convert_ImageWithExistingAssetFile_OverwritesAssetFile()
    {
        // Arrange
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var assetsFolder = Path.Combine(tempRoot, "assets");
        var existingAssetPath = Path.Combine(assetsFolder, "page_image_0001.png");
        var xml = @"<?xml version=""1.0""?>
            <one:Page xmlns:one=""http://schemas.microsoft.com/office/onenote/2013/onenote"">
                <one:Image format=""png""><one:Data>AQID</one:Data></one:Image>
            </one:Page>";

        try
        {
            Directory.CreateDirectory(assetsFolder);
            File.WriteAllBytes(existingAssetPath, new byte[] { 9, 9, 9 });

            // Act
            var result = _converter.Convert(xml, assetsFolder, "assets", null, "page");

            // Assert
            result.Should().Contain("assets/page_image_0001.png");
            File.ReadAllBytes(existingAssetPath).Should().Equal(new byte[] { 1, 2, 3 });
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }

    #endregion

    #region Cleanup Tests

    [Fact]
    public void Convert_MultipleBlankLines_ReducedToTwo()
    {
        // Arrange
        var xml = @"<?xml version=""1.0""?>
            <one:Page xmlns:one=""http://schemas.microsoft.com/office/onenote/2013/onenote"">
                <one:Outline>
                    <one:OEChildren>
                        <one:OE><one:T><![CDATA[Line 1]]></one:T></one:OE>
                        <one:OE><one:T><![CDATA[]]></one:T></one:OE>
                        <one:OE><one:T><![CDATA[]]></one:T></one:OE>
                        <one:OE><one:T><![CDATA[]]></one:T></one:OE>
                        <one:OE><one:T><![CDATA[]]></one:T></one:OE>
                        <one:OE><one:T><![CDATA[Line 2]]></one:T></one:OE>
                    </one:OEChildren>
                </one:Outline>
            </one:Page>";

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "test");

        // Assert
        // Should not have more than 2 consecutive newlines (3+ newline chars in a row)
        result.Should().NotContain("\n\n\n\n");
    }

    [Fact]
    public void Convert_HtmlEntities_Decoded()
    {
        // Arrange
        var xml = CreatePageXml(@"<one:T><![CDATA[Tom &amp; Jerry]]></one:T>");

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "test");

        // Assert
        result.Should().Contain("Tom & Jerry");
    }

    [Fact]
    public void Convert_UnicodeContent_Preserved()
    {
        // Arrange
        var xml = CreatePageXml(@"<one:T><![CDATA[Hello 世界 🎉]]></one:T>");

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "test");

        // Assert
        result.Should().Contain("Hello 世界 🎉");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Convert_NullBinaryFetcher_HandlesGracefully()
    {
        // Arrange
        var xml = CreatePageXml(@"<one:T><![CDATA[Simple text]]></one:T>");

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "test");

        // Assert
        result.Should().NotBeNull();
        result.Should().Contain("Simple text");
    }

    [Fact]
    public void Convert_EmptyPrefix_HandlesGracefully()
    {
        // Arrange
        var xml = CreatePageXml(@"<one:T><![CDATA[Content]]></one:T>");

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "");

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void Convert_SpecialCharsInPrefix_Sanitized()
    {
        // Arrange
        var xml = CreatePageXml(@"<one:T><![CDATA[Content]]></one:T>");

        // Act - prefix with invalid filename characters
        var result = _converter.Convert(xml, "", "assets", null, "test:page/name");

        // Assert
        result.Should().NotBeNull();
    }

    #endregion

    #region Timestamp Tests

    [Fact]
    public void Convert_WithLastModifiedTime_EmitsItalicLineAfterTitle()
    {
        // Arrange
        var xml = @"<?xml version=""1.0""?>
            <one:Page xmlns:one=""http://schemas.microsoft.com/office/onenote/2013/onenote"">
                <one:Title>
                    <one:OE><one:T><![CDATA[My Page]]></one:T></one:OE>
                </one:Title>
                <one:Outline>
                    <one:OEChildren>
                        <one:OE><one:T><![CDATA[Body text]]></one:T></one:OE>
                    </one:OEChildren>
                </one:Outline>
            </one:Page>";
        var lastModified = new DateTimeOffset(2024, 1, 15, 14, 30, 0, TimeSpan.FromHours(3));

        // Act
        var result = _converter.Convert(xml, "", "assets", lastModified, true, null, "test");

        // Assert
        result.Should().Contain("# My Page");
        result.Should().Contain("*Last modified: 2024-01-15T14:30:00+03:00*");
        // Timestamp line should appear between the title and the body
        var titleIndex = result.IndexOf("# My Page", StringComparison.Ordinal);
        var timestampIndex = result.IndexOf("Last modified", StringComparison.Ordinal);
        var bodyIndex = result.IndexOf("Body text", StringComparison.Ordinal);
        titleIndex.Should().BeLessThan(timestampIndex);
        timestampIndex.Should().BeLessThan(bodyIndex);
    }

    [Fact]
    public void Convert_WithoutLastModifiedTime_DoesNotEmitItalicLine()
    {
        // Arrange
        var xml = @"<?xml version=""1.0""?>
            <one:Page xmlns:one=""http://schemas.microsoft.com/office/onenote/2013/onenote"">
                <one:Title>
                    <one:OE><one:T><![CDATA[My Page]]></one:T></one:OE>
                </one:Title>
                <one:Outline>
                    <one:OEChildren>
                        <one:OE><one:T><![CDATA[Body text]]></one:T></one:OE>
                    </one:OEChildren>
                </one:Outline>
            </one:Page>";

        // Act
        var result = _converter.Convert(xml, "", "assets", null, true, null, "test");

        // Assert
        result.Should().NotContain("Last modified");
    }

    [Fact]
    public void Convert_IncludeTimestampsFalse_SkipsItalicEvenWhenTimestampPresent()
    {
        // Arrange
        var xml = @"<?xml version=""1.0""?>
            <one:Page xmlns:one=""http://schemas.microsoft.com/office/onenote/2013/onenote"">
                <one:Title>
                    <one:OE><one:T><![CDATA[My Page]]></one:T></one:OE>
                </one:Title>
                <one:Outline>
                    <one:OEChildren>
                        <one:OE><one:T><![CDATA[Body]]></one:T></one:OE>
                    </one:OEChildren>
                </one:Outline>
            </one:Page>";
        var lastModified = new DateTimeOffset(2024, 1, 15, 14, 30, 0, TimeSpan.FromHours(0));

        // Act
        var result = _converter.Convert(xml, "", "assets", lastModified, false, null, "test");

        // Assert
        result.Should().NotContain("Last modified");
    }

    [Fact]
    public void Convert_FormatsTimestampAsLocalIso8601WithOffset()
    {
        // Arrange
        var xml = CreatePageXml(@"<one:T><![CDATA[Content]]></one:T>");
        // A fixed UTC instant; the rendered string should reflect the *local* time at that instant plus the local offset.
        var utcInstant = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var expected = utcInstant.LocalDateTime.ToString("yyyy-MM-ddTHH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture);

        // Act
        var result = _converter.Convert(xml, "", "assets", utcInstant, true, null, "test");

        // Assert
        result.Should().Contain($"*Last modified: {expected}*");
    }

    [Fact]
    public void Convert_LegacyOverload_DefaultsToIncludeTimestampsWithoutValue()
    {
        // Arrange - legacy 5-arg overload is preserved (no timestamp) and should not emit the italic line.
        var xml = @"<?xml version=""1.0""?>
            <one:Page xmlns:one=""http://schemas.microsoft.com/office/onenote/2013/onenote"">
                <one:Title>
                    <one:OE><one:T><![CDATA[My Page]]></one:T></one:OE>
                </one:Title>
                <one:Outline>
                    <one:OEChildren>
                        <one:OE><one:T><![CDATA[Body]]></one:T></one:OE>
                    </one:OEChildren>
                </one:Outline>
            </one:Page>";

        // Act
        var result = _converter.Convert(xml, "", "assets", null, "test");

        // Assert
        result.Should().NotContain("Last modified");
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a minimal OneNote page XML with the given content element.
    /// </summary>
    private static string CreatePageXml(string contentElement)
    {
        return $@"<?xml version=""1.0""?>
            <one:Page xmlns:one=""http://schemas.microsoft.com/office/onenote/2013/onenote"">
                <one:Outline>
                    <one:OEChildren>
                        <one:OE>{contentElement}</one:OE>
                    </one:OEChildren>
                </one:Outline>
            </one:Page>";
    }

    /// <summary>
    /// Creates a OneNote page XML with text that has a style attribute on the T element.
    /// </summary>
    private static string CreatePageXmlWithStyle(string text, string style)
    {
        return $@"<?xml version=""1.0""?>
            <one:Page xmlns:one=""http://schemas.microsoft.com/office/onenote/2013/onenote"">
                <one:Outline>
                    <one:OEChildren>
                        <one:OE><one:T style=""{style}""><![CDATA[{text}]]></one:T></one:OE>
                    </one:OEChildren>
                </one:Outline>
            </one:Page>";
    }

    #endregion
}
