using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HtmlAgilityPack;
using ReverseMarkdown;

namespace OneNoteMarkdownExporter.Services
{
    /// <summary>
    /// Delegate for fetching binary content from OneNote using a callback ID.
    /// </summary>
    /// <param name="callbackId">The callback ID of the binary object.</param>
    /// <returns>Base64-encoded string of the binary content, or null if not available.</returns>
    public delegate string? BinaryContentFetcher(string callbackId);

    /// <summary>
    /// Converts OneNote page XML directly to Markdown without using the Publish API.
    /// This bypasses DLP/sensitivity label restrictions that block the Publish() method.
    /// Uses ReverseMarkdown for proper HTML-to-Markdown conversion.
    /// </summary>
    public class OneNoteXmlToMarkdownConverter : IMarkdownContentConverter
    {
        private readonly XNamespace _ns = "http://schemas.microsoft.com/office/onenote/2013/onenote";
        private readonly Converter _markdownConverter;
        private string _assetsFolder = "";
        private string _relativeAssetsPath = "";
        private string _pagePrefix = "";
        private int _imageCounter = 0;
        private string _literalPlaceholderPrefix = "";
        private readonly List<(string Token, string Markdown)> _literalPlaceholders = new();
        private BinaryContentFetcher? _binaryContentFetcher;

        public OneNoteXmlToMarkdownConverter()
        {
            var config = new ReverseMarkdown.Config
            {
                UnknownTags = Config.UnknownTagsOption.Drop,
                GithubFlavored = true,
                RemoveComments = true,
                SmartHrefHandling = true,
                EscapeMarkdownLineStarts = true
            };
            _markdownConverter = new Converter(config);
        }

        public string Convert(string pageXml, string assetsFolder, string relativeAssetsPath, BinaryContentFetcher? binaryContentFetcher = null, string? pagePrefix = null)
        {
            _assetsFolder = assetsFolder;
            _relativeAssetsPath = relativeAssetsPath;
            _pagePrefix = SanitizePrefix(pagePrefix);
            _imageCounter = 0;
            _literalPlaceholderPrefix = $"ONENOTELITERAL{Guid.NewGuid():N}";
            _literalPlaceholders.Clear();
            _binaryContentFetcher = binaryContentFetcher;

            var doc = XDocument.Parse(pageXml);
            if (doc.Root == null) return "";

            // Build HTML first, then convert to clean Markdown using ReverseMarkdown
            var htmlBuilder = new StringBuilder();
            htmlBuilder.AppendLine("<html><body>");

            // Get page title
            var titleElement = doc.Root.Element(_ns + "Title");
            if (titleElement != null)
            {
                var titleText = GetPlainText(titleElement.Element(_ns + "OE"));
                if (!string.IsNullOrWhiteSpace(titleText))
                {
                    htmlBuilder.AppendLine($"<h1>{System.Net.WebUtility.HtmlEncode(titleText.Trim())}</h1>");
                }
            }

            // Process all Outline elements (main content containers)
            foreach (var outline in doc.Root.Elements(_ns + "Outline"))
            {
                ProcessOutline(outline, htmlBuilder);
            }

            // Process any images directly on the page (outside outlines)
            foreach (var image in doc.Root.Elements(_ns + "Image"))
            {
                ProcessImage(image, htmlBuilder);
            }

            htmlBuilder.AppendLine("</body></html>");

            // Get the HTML and normalize anchor tags BEFORE ReverseMarkdown processing
            var html = htmlBuilder.ToString();
            html = NormalizeHtmlAnchors(html);
            html = NormalizeHtmlHeadings(html);

            // Convert HTML to Markdown using ReverseMarkdown library
            var markdown = _markdownConverter.Convert(html);

            // Final cleanup
            markdown = CleanupMarkdown(markdown);

            return markdown;
        }

        private static string SanitizePrefix(string? prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix))
            {
                return "";
            }

            return ExportPathSanitizer.SanitizeComponent(prefix, "page", prefix).Replace(' ', '_');
        }

        /// <summary>
        /// Normalizes HTML anchor tags to ensure they're on single lines
        /// so ReverseMarkdown can process them correctly.
        /// </summary>
        private string NormalizeHtmlAnchors(string html)
        {
            // Find all <a ...>...</a> tags and normalize them to single lines
            // This regex captures the entire anchor tag including content
            html = Regex.Replace(html,
                @"<a\s([^>]*?)>",
                match => {
                    // Normalize whitespace in the opening tag
                    var attributes = match.Groups[1].Value;
                    attributes = Regex.Replace(attributes, @"\s+", " ").Trim();
                    return $"<a {attributes}>";
                },
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            return html;
        }

        private static string NormalizeHtmlHeadings(string html)
        {
            var document = new HtmlAgilityPack.HtmlDocument();
            document.LoadHtml(html);
            var hasTopHeading = false;
            var demoteSection = false;

            foreach (var heading in document.DocumentNode.Descendants()
                .Where(node => node.Name is "h1" or "h2" or "h3" or "h4" or "h5" or "h6")
                .ToList())
            {
                var level = heading.Name[1] - '0';
                if (level == 1)
                {
                    demoteSection = hasTopHeading;
                    hasTopHeading = true;
                }

                if (demoteSection)
                {
                    heading.Name = $"h{Math.Min(6, level + 1)}";
                }
            }

            return document.DocumentNode.InnerHtml;
        }

        private void ProcessOutline(XElement outline, StringBuilder html)
        {
            foreach (var oeChildren in outline.Elements(_ns + "OEChildren"))
            {
                ProcessOEChildren(oeChildren, html);
            }
        }

        private void ProcessOEChildren(XElement oeChildren, StringBuilder html)
        {
            // Check if this is a list context
            var elements = oeChildren.Elements(_ns + "OE").ToList();
            
            bool inBulletList = false;
            bool inNumberedList = false;

            foreach (var oe in elements)
            {
                var listElement = oe.Element(_ns + "List");
                bool isBullet = listElement?.Element(_ns + "Bullet") != null;
                bool isNumbered = listElement?.Element(_ns + "Number") != null;

                // Check if this OE has any real content (not just whitespace)
                bool hasContent = HasRealContent(oe);

                // Handle list transitions
                if (isBullet && !inBulletList)
                {
                    if (inNumberedList) { html.AppendLine("</ol>"); inNumberedList = false; }
                    html.AppendLine("<ul>");
                    inBulletList = true;
                }
                else if (isNumbered && !inNumberedList)
                {
                    if (inBulletList) { html.AppendLine("</ul>"); inBulletList = false; }
                    html.AppendLine("<ol>");
                    inNumberedList = true;
                }
                else if (!isBullet && !isNumbered && (inBulletList || inNumberedList))
                {
                    // Only close the list if this is a non-empty paragraph
                    // Empty paragraphs (blank lines) should not break list continuity
                    if (hasContent)
                    {
                        if (inBulletList) { html.AppendLine("</ul>"); inBulletList = false; }
                        if (inNumberedList) { html.AppendLine("</ol>"); inNumberedList = false; }
                    }
                    // If no content, just skip - don't close the list
                }

                // Only process if it has content or is a list item
                if (hasContent || isBullet || isNumbered)
                {
                    ProcessOE(oe, html, inBulletList || inNumberedList);
                }
            }

            // Close any open lists
            if (inBulletList) html.AppendLine("</ul>");
            if (inNumberedList) html.AppendLine("</ol>");
        }

        /// <summary>
        /// Checks if an OE element has any real text content (not just whitespace or empty elements)
        /// </summary>
        private bool HasRealContent(XElement oe)
        {
            // Check text elements
            foreach (var t in oe.Elements(_ns + "T"))
            {
                var cdata = t.Nodes().OfType<XCData>().FirstOrDefault();
                var text = cdata?.Value ?? t.Value;
                // Strip HTML tags and check if there's real content
                text = Regex.Replace(text, "<[^>]+>", "");
                if (!string.IsNullOrWhiteSpace(text))
                    return true;
            }
            
            // Check for images
            if (oe.Elements(_ns + "Image").Any())
                return true;
            
            // Check for tables
            if (oe.Elements(_ns + "Table").Any())
                return true;
            
            // Check nested children
            var nestedChildren = oe.Element(_ns + "OEChildren");
            if (nestedChildren != null && nestedChildren.Elements(_ns + "OE").Any())
            {
                foreach (var child in nestedChildren.Elements(_ns + "OE"))
                {
                    if (HasRealContent(child))
                        return true;
                }
            }

            return false;
        }

        private void ProcessOE(XElement oe, StringBuilder html, bool inList)
        {
            var listElement = oe.Element(_ns + "List");
            bool isListItem = listElement != null && 
                (listElement.Element(_ns + "Bullet") != null || listElement.Element(_ns + "Number") != null);

            // Build content for this element
            var content = new StringBuilder();

            // Process text elements
            foreach (var t in oe.Elements(_ns + "T"))
            {
                content.Append(ProcessTextElement(t));
            }

            // Process tables
            foreach (var table in oe.Elements(_ns + "Table"))
            {
                content.Append(ProcessTable(table));
            }

            // Process images
            foreach (var image in oe.Elements(_ns + "Image"))
            {
                content.Append(ProcessImageToHtml(image));
            }

            var textContent = content.ToString();
            bool hasContent = !string.IsNullOrWhiteSpace(Regex.Replace(textContent, "<[^>]*>", "").Trim());

            if (hasContent || content.Length > 0)
            {
                if (isListItem || inList)
                {
                    html.Append("<li>");
                    html.Append(textContent);
                }
                else
                {
                    html.Append("<p>");
                    html.Append(textContent);
                    html.AppendLine("</p>");
                }
            }

            // Process nested children
            var nestedChildren = oe.Element(_ns + "OEChildren");
            if (nestedChildren != null)
            {
                if (isListItem || inList)
                {
                    // Nested content within list item
                    ProcessOEChildren(nestedChildren, html);
                }
                else
                {
                    ProcessOEChildren(nestedChildren, html);
                }
            }

            if ((hasContent || content.Length > 0) && (isListItem || inList))
            {
                html.AppendLine("</li>");
            }
        }

        private string ProcessTextElement(XElement t, bool inTable = false)
        {
            var cdata = t.Nodes().OfType<XCData>().FirstOrDefault();
            var rawText = cdata?.Value ?? t.Value;

            if (string.IsNullOrEmpty(rawText)) return "";

            var html = ConvertOneNoteStylesToHtml(rawText, inTable);
            var style = t.Attribute("style")?.Value ?? "";

            if (style.Contains("font-weight:bold"))
            {
                html = $"<strong>{html}</strong>";
            }
            if (style.Contains("font-style:italic"))
            {
                html = $"<em>{html}</em>";
            }
            if (style.Contains("text-decoration:line-through"))
            {
                html = $"<del>{html}</del>";
            }

            return html;
        }
        private string ConvertOneNoteStylesToHtml(string html, bool inTable)
        {
            var fragment = new HtmlAgilityPack.HtmlDocument();
            fragment.LoadHtml(html);

            foreach (var span in fragment.DocumentNode.Descendants("span").Reverse().ToList())
            {
                var styles = ParseStyles(span.GetAttributeValue("style", ""));
                var tags = new List<string>();

                if (styles.ContainsKey("background")
                    || styles.TryGetValue("font-weight", out var fontWeight) && fontWeight.Equals("bold", StringComparison.OrdinalIgnoreCase))
                {
                    tags.Add("strong");
                }
                if (styles.TryGetValue("font-style", out var fontStyle) && fontStyle.Equals("italic", StringComparison.OrdinalIgnoreCase))
                {
                    tags.Add("em");
                }
                if (styles.TryGetValue("text-decoration", out var textDecoration)
                    && textDecoration.Contains("line-through", StringComparison.OrdinalIgnoreCase))
                {
                    tags.Add("del");
                }

                if (tags.Count == 0)
                {
                    span.ParentNode.RemoveChild(span, true);
                    continue;
                }

                span.Name = tags[0];
                span.Attributes.RemoveAll();
                var current = span;

                foreach (var tag in tags.Skip(1))
                {
                    var wrapper = fragment.CreateElement(tag);
                    foreach (var child in current.ChildNodes.ToList())
                    {
                        current.RemoveChild(child);
                        wrapper.AppendChild(child);
                    }
                    current.AppendChild(wrapper);
                    current = wrapper;
                }
            }

            ProtectLiteralText(fragment, inTable);

            return fragment.DocumentNode.InnerHtml;
        }

        private void ProtectLiteralText(HtmlAgilityPack.HtmlDocument fragment, bool inTable)
        {
            foreach (var code in fragment.DocumentNode.Descendants()
                .Where(node => node.Name is "code" or "pre"
                    && !node.Ancestors().Any(ancestor => ancestor.Name is "code" or "pre"))
                .ToList())
            {
                var codeText = HtmlEntity.DeEntitize(code.InnerText);
                var markdown = inTable && (codeText.Contains('|') || code.Name == "pre")
                    ? GetTableCodeMarkdown(codeText, code.Name == "pre")
                    : _markdownConverter.ConvertNode(code).Replace("\r\n", "\n").Trim('\n');
                HtmlNode replacement = fragment.CreateTextNode(CreateLiteralPlaceholder("CODE", markdown));
                if (code.Name == "pre")
                {
                    var paragraph = fragment.CreateElement("p");
                    paragraph.AppendChild(replacement);
                    replacement = paragraph;
                }
                code.ParentNode.ReplaceChild(replacement, code);
            }

            if (inTable)
            {
                foreach (var attribute in fragment.DocumentNode.Descendants().SelectMany(node => node.Attributes)
                    .Where(attribute => attribute.Name is "href" or "src"))
                {
                    attribute.Value = attribute.Value.Replace("|", "%7C");
                }
            }

            foreach (var textNode in fragment.DocumentNode.Descendants().OfType<HtmlTextNode>().ToList())
            {
                var text = HtmlEntity.DeEntitize(textNode.Text);
                if (inTable && text.Contains('|'))
                {
                    text = text.Replace("|", CreateLiteralPlaceholder("PIPE", @"\|"));
                }
                textNode.Text = System.Net.WebUtility.HtmlEncode(
                    ProtectLiteralPlainText(text, escapeBrackets: !textNode.Ancestors("a").Any()));
            }
        }

        private static string GetTableCodeMarkdown(string text, bool block)
        {
            var encoded = Regex.Replace(text, @"[&<>\\`*_\[\]|]", match => $"&#{(int)match.Value[0]};")
                .Replace("\r\n", "\n").Replace("\r", "\n").Replace("\t", "&#9;")
                .Replace("\n", block ? "&#10;" : "<br>");
            return block ? $"<pre><code>{encoded}</code></pre>" : $"<code>{encoded}</code>";
        }

        private string CreateLiteralPlaceholder(string kind, string replacement)
        {
            var placeholder = $"{_literalPlaceholderPrefix}{kind}{_literalPlaceholders.Count}END";
            _literalPlaceholders.Add((placeholder, replacement));
            return placeholder;
        }

        private static bool ShouldEscapeLiteralLessThan(string text, int startIndex)
        {
            var endIndex = FindClosingAngleBracket(text, startIndex + 1);
            if (endIndex < 0)
            {
                return false;
            }

            var candidate = text[startIndex..(endIndex + 1)];
            if (candidate.StartsWith("<!--", StringComparison.Ordinal)
                || candidate.StartsWith("<?", StringComparison.Ordinal)
                || candidate.StartsWith("<![CDATA[", StringComparison.OrdinalIgnoreCase)
                || candidate.Length > 2 && candidate[1] == '!' && char.IsAsciiLetterUpper(candidate[2]))
            {
                return true;
            }

            return Regex.IsMatch(
                candidate,
                @"^(?:<[A-Za-z][A-Za-z0-9-]*(?:\s+[A-Za-z_:][A-Za-z0-9_.:-]*(?:\s*=\s*(?:[^\""'=<>`\s]+|'[^']*'|\""[^\""']*\""))?)*\s*/?>|</[A-Za-z][A-Za-z0-9-]*\s*>)$",
                RegexOptions.Singleline);
        }

        private static int FindClosingAngleBracket(string text, int startIndex)
        {
            char? quote = null;

            for (var index = startIndex; index < text.Length; index++)
            {
                var character = text[index];
                if (quote.HasValue)
                {
                    if (character == quote.Value)
                    {
                        quote = null;
                    }
                }
                else if (character is '\'' or '"')
                {
                    quote = character;
                }
                else if (character == '>')
                {
                    return index;
                }
            }

            return -1;
        }

        private static bool ShouldEscapeLiteralGreaterThan(string text, int index)
        {
            var lineStart = index > 0 ? text.LastIndexOf('\n', index - 1) + 1 : 0;
            return text.AsSpan(lineStart, index - lineStart).Trim().IsEmpty;
        }

        private static Dictionary<string, string> ParseStyles(string style)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var declaration in style.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = declaration.Split(':', 2);
                if (parts.Length == 2)
                {
                    result[parts[0].Trim()] = parts[1].Trim();
                }
            }

            return result;
        }

        private void ProcessImage(XElement image, StringBuilder html)
        {
            html.Append(ProcessImageToHtml(image));
        }

        private string ProcessImageToHtml(XElement image)
        {
            try
            {
                string? base64Data = null;
                
                // First, try to get embedded data from the Data element
                var dataElement = image.Element(_ns + "Data");
                if (dataElement != null && !string.IsNullOrWhiteSpace(dataElement.Value))
                {
                    base64Data = dataElement.Value.Trim();
                }
                
                // If no embedded data, try to fetch using callbackID
                if (string.IsNullOrWhiteSpace(base64Data))
                {
                    var callbackId = image.Attribute("callbackID")?.Value;
                    if (!string.IsNullOrWhiteSpace(callbackId) && _binaryContentFetcher != null)
                    {
                        base64Data = _binaryContentFetcher(callbackId);
                    }
                }
                
                // If we still don't have data, return a placeholder
                if (string.IsNullOrWhiteSpace(base64Data))
                {
                    // Log additional info for debugging
                    var callbackId = image.Attribute("callbackID")?.Value;
                    var objectId = image.Attribute("objectID")?.Value;
                    var info = $"callbackID={callbackId ?? "none"}, objectID={objectId ?? "none"}";
                    return $"<p><em>[Image - no embedded data, could not fetch binary content. {System.Net.WebUtility.HtmlEncode(info)}]</em></p>";
                }
                
                // Remove any whitespace from base64
                base64Data = Regex.Replace(base64Data, @"\s+", "");

                // Determine format
                var format = image.Attribute("format")?.Value?.ToLower() ?? "png";
                var extension = format switch
                {
                    "png" => ".png",
                    "jpg" or "jpeg" => ".jpg",
                    "gif" => ".gif",
                    "bmp" => ".bmp",
                    "emf" => ".png", // Convert EMF reference to PNG
                    "wmf" => ".png", // Convert WMF reference to PNG
                    _ => ".png"
                };

                // Generate unique filename with page prefix to avoid collisions across pages
                _imageCounter++;
                var fileName = ExportPathSanitizer.GetSafeAssetFileName(_assetsFolder, _pagePrefix, _imageCounter, extension);
                var filePath = Path.Combine(_assetsFolder, fileName);

                // Ensure assets folder exists
                if (!Directory.Exists(_assetsFolder))
                {
                    Directory.CreateDirectory(_assetsFolder);
                }

                // Decode and save
                var imageBytes = System.Convert.FromBase64String(base64Data);
                File.WriteAllBytes(filePath, imageBytes);

                // Return HTML img tag
                var relativePath = $"{_relativeAssetsPath}/{fileName}".Replace("\\", "/");
                return $"<p><img src=\"{relativePath}\" alt=\"image\" /></p>";
            }
            catch (Exception ex)
            {
                return $"<p><em>[Image export failed: {System.Net.WebUtility.HtmlEncode(ex.Message)}]</em></p>";
            }
        }

        private string ProcessTable(XElement table)
        {
            var rows = table.Elements(_ns + "Row").ToList();
            if (!rows.Any()) return "";
            var columnCount = rows.Max(row => row.Elements(_ns + "Cell").Count());

            var sb = new StringBuilder();
            sb.AppendLine("<table>");

            bool isFirstRow = true;
            foreach (var row in rows)
            {
                sb.AppendLine("<tr>");
                var cells = row.Elements(_ns + "Cell").ToList();
                for (var column = 0; column < columnCount; column++)
                {
                    var tag = isFirstRow ? "th" : "td";
                    var cellContent = column < cells.Count ? GetCellContent(cells[column]) : string.Empty;
                    sb.AppendLine($"<{tag}>{cellContent}</{tag}>");
                }
                sb.AppendLine("</tr>");
                isFirstRow = false;
            }

            sb.AppendLine("</table>");
            return sb.ToString();
        }

        private string GetCellContent(XElement cell)
        {
            var oeChildren = cell.Element(_ns + "OEChildren");
            if (oeChildren == null) return "";

            var parts = new List<string>();
            foreach (var oe in oeChildren.Elements(_ns + "OE"))
            {
                var text = new StringBuilder();
                foreach (var t in oe.Elements(_ns + "T"))
                {
                    text.Append(ProcessTextElement(t, inTable: true));
                }
                if (text.Length > 0)
                {
                    parts.Add(text.ToString());
                }
            }

            return string.Join("<br/>", parts);
        }

        private string GetPlainText(XElement? oe)
        {
            if (oe == null) return "";

            var sb = new StringBuilder();
            foreach (var t in oe.Elements(_ns + "T"))
            {
                var cdata = t.Nodes().OfType<XCData>().FirstOrDefault();
                var text = cdata?.Value ?? t.Value;

                var fragment = new HtmlAgilityPack.HtmlDocument();
                fragment.LoadHtml(text);
                sb.Append(ProtectLiteralPlainText(HtmlEntity.DeEntitize(fragment.DocumentNode.InnerText)));
            }
            return sb.ToString();
        }

        private string ProtectLiteralPlainText(string text, bool escapeBrackets = true)
        {
            var protectedText = new StringBuilder(text.Length);

            for (var index = 0; index < text.Length; index++)
            {
                if (text[index] == '<')
                {
                    var endIndex = FindClosingAngleBracket(text, index + 1);
                    if (endIndex >= 0)
                    {
                        var candidate = text[index..(endIndex + 1)];
                        if (IsLiteralHttpAutolink(candidate))
                        {
                            protectedText.Append(CreateLiteralPlaceholder("AUTOLINK", candidate));
                            index = endIndex;
                            continue;
                        }
                        if (ShouldEscapeLiteralLessThan(text, index))
                        {
                            protectedText.Append(CreateLiteralPlaceholder("HTML", $"\\{candidate}"));
                            index = endIndex;
                            continue;
                        }
                    }

                    protectedText.Append(CreateLiteralPlaceholder("LT", "<"));
                }
                else if (text[index] == '>')
                {
                    var replacement = ShouldEscapeLiteralGreaterThan(text, index) ? @"\>" : ">";
                    protectedText.Append(CreateLiteralPlaceholder("GT", replacement));
                }
                else if (text[index] == '\\')
                {
                    protectedText.Append(CreateLiteralPlaceholder("BACKSLASH", @"\\"));
                }
                else if (escapeBrackets && text[index] is '[' or ']')
                {
                    protectedText.Append(CreateLiteralPlaceholder("BRACKET", $"\\{text[index]}"));
                }
                else
                {
                    protectedText.Append(text[index]);
                }
            }

            return protectedText.ToString();
        }

        private static bool IsLiteralHttpAutolink(string candidate)
        {
            return Regex.IsMatch(candidate, @"^<https?://[^\s<>]+>$", RegexOptions.IgnoreCase);
        }

        private string CleanupMarkdown(string markdown)
        {
            // Aggressively find and convert ALL <a>...</a> tags to Markdown links
            // This regex handles any whitespace/newlines within the tag
            markdown = ConvertAllAnchorTags(markdown);

            // Fix escaped underscores in existing Markdown links [text](url)
            // URLs should not have escaped underscores
            markdown = Regex.Replace(markdown,
                @"\]\(([^)]+)\)",
                match => {
                    var url = match.Groups[1].Value;
                    url = HtmlEntity.DeEntitize(url).Replace("\\_", "_");
                    return $"]({url})";
                });

            // Convert naked URL links [url](url) to <url> format
            // This handles cases where the link text matches the URL
            markdown = Regex.Replace(markdown, 
                @"\[([^\]]+)\]\((\1)\)", 
                match => {
                    var url = match.Groups[1].Value;
                    return $"<{url}>";
                });
            
            // Also handle URL-encoded variations where link text is URL-decoded version
            markdown = Regex.Replace(markdown,
                @"\[(https?://[^\]]+)\]\((https?://[^\)]+)\)",
                match => {
                    var linkText = match.Groups[1].Value;
                    var href = match.Groups[2].Value;
                    // Normalize both by decoding and comparing
                    var decodedText = Uri.UnescapeDataString(linkText.Replace("\\_", "_"));
                    var decodedHref = Uri.UnescapeDataString(href.Replace("\\_", "_"));
                    if (decodedText == decodedHref || linkText == href)
                    {
                        return $"<{href}>";
                    }
                    return match.Value; // Keep original if they differ
                });

            // Remove excessive blank lines
            markdown = Regex.Replace(markdown, @"\n{3,}", "\n\n");
            
            markdown = WrapBareUrls(markdown);
            
            // Remove empty paragraphs
            markdown = Regex.Replace(markdown, @"\n\n\n+", "\n\n");
            
            // Trim lines
            var lines = markdown.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = lines[i].TrimEnd();
            }
            
            markdown = string.Join("\n", lines).Trim();

            // Restore enclosing literals before any placeholders nested inside them.
            for (var index = _literalPlaceholders.Count - 1; index >= 0; index--)
            {
                var placeholder = _literalPlaceholders[index];
                markdown = markdown.Replace(placeholder.Token, placeholder.Markdown, StringComparison.Ordinal);
            }

            return markdown;
        }

        private string WrapBareUrls(string markdown)
        {
            return Regex.Replace(markdown,
                @"https?://[^\s<>\]]+",
                match => {
                    if (IsAlreadyAutolink(markdown, match.Index)
                        || IsMarkdownLinkDestination(markdown, match.Index))
                    {
                        return match.Value;
                    }

                    var url = match.Value.Replace("\\_", "_").Replace("\\*", "*");
                    var trailing = "";

                    while (url.Length > 0 && IsTrailingUrlPunctuation(url[^1]))
                    {
                        trailing = url[^1] + trailing;
                        url = url[..^1];
                    }

                    return url.Length == 0 ? match.Value : $"<{url}>{trailing}";
                });
        }

        private static bool IsAlreadyAutolink(string markdown, int urlStartIndex)
        {
            return urlStartIndex > 0 && markdown[urlStartIndex - 1] == '<';
        }

        private static bool IsMarkdownLinkDestination(string markdown, int urlStartIndex)
        {
            return urlStartIndex > 1 && markdown[urlStartIndex - 1] == '(' && markdown[urlStartIndex - 2] == ']';
        }

        private static bool IsTrailingUrlPunctuation(char character)
        {
            return character == '.'
                || character == ','
                || character == ';'
                || character == ':'
                || character == '!'
                || character == '?'
                || character == ')';
        }

        /// <summary>
        /// Finds and converts all HTML anchor tags to Markdown links.
        /// Handles multiline tags and various attribute formats.
        /// </summary>
        private string ConvertAllAnchorTags(string markdown)
        {
            // Use a loop to find and replace anchor tags one at a time
            // This handles complex cases that regex struggles with
            while (true)
            {
                // Find the start of an anchor tag
                int startIdx = markdown.IndexOf("<a", StringComparison.OrdinalIgnoreCase);
                if (startIdx == -1) break;

                // Find the closing </a>
                int endIdx = markdown.IndexOf("</a>", startIdx, StringComparison.OrdinalIgnoreCase);
                if (endIdx == -1) break;

                int fullEndIdx = endIdx + 4; // Include "</a>"

                // Extract the full anchor tag
                string anchorTag = markdown.Substring(startIdx, fullEndIdx - startIdx);

                // Parse out the href
                string? href = null;
                var hrefMatch = Regex.Match(anchorTag, @"href\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (hrefMatch.Success)
                {
                    href = hrefMatch.Groups[1].Value.Trim();
                }

                // Parse out the link text (content between > and </a>)
                string? linkText = null;
                int contentStart = anchorTag.IndexOf('>');
                if (contentStart != -1)
                {
                    int contentEnd = anchorTag.LastIndexOf("</a>", StringComparison.OrdinalIgnoreCase);
                    if (contentEnd > contentStart)
                    {
                        linkText = anchorTag.Substring(contentStart + 1, contentEnd - contentStart - 1).Trim();
                    }
                }

                // Convert to Markdown link
                string replacement;
                if (!string.IsNullOrEmpty(href) && !string.IsNullOrEmpty(linkText))
                {
                    // Unescape underscores in URL
                    href = href.Replace("\\_", "_");
                    replacement = ConvertToMarkdownLink(href, linkText);
                }
                else if (!string.IsNullOrEmpty(href))
                {
                    href = href.Replace("\\_", "_");
                    replacement = $"<{href}>";
                }
                else
                {
                    // Can't parse, just remove the tags and keep content
                    replacement = linkText ?? "";
                }

                // Replace the anchor tag with the Markdown link
                markdown = markdown.Substring(0, startIdx) + replacement + markdown.Substring(fullEndIdx);
            }

            return markdown;
        }

        /// <summary>
        /// Converts href and link text to proper Markdown link format.
        /// If text matches the URL (naked URL), uses angle bracket format.
        /// Otherwise uses standard [text](url) format.
        /// </summary>
        private string ConvertToMarkdownLink(string href, string text)
        {
            // Normalize for comparison
            var normalizedText = Uri.UnescapeDataString(text.Replace("\\_", "_").Replace("\\", ""));
            var normalizedHref = Uri.UnescapeDataString(href.Replace("\\_", "_").Replace("\\", ""));

            // Check if this is a naked URL (link text matches URL)
            if (normalizedText == normalizedHref || text == href || 
                text.TrimEnd('/') == href.TrimEnd('/') ||
                normalizedText.TrimEnd('/') == normalizedHref.TrimEnd('/'))
            {
                // Naked URL - use angle bracket format
                return $"<{href}>";
            }

            // Standard link with different text
            return $"[{text}]({href})";
        }
    }
}
