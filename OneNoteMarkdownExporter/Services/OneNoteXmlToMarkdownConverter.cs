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
    /// Converts OneNote page XML directly to Markdown without using the Publish API.
    /// This bypasses DLP/sensitivity label restrictions that block the Publish() method.
    /// Uses ReverseMarkdown for proper HTML-to-Markdown conversion.
    /// </summary>
    public class OneNoteXmlToMarkdownConverter
    {
        private readonly XNamespace _ns = "http://schemas.microsoft.com/office/onenote/2013/onenote";
        private readonly Converter _markdownConverter;
        private string _assetsFolder = "";
        private string _relativeAssetsPath = "";
        private int _imageCounter = 0;

        public OneNoteXmlToMarkdownConverter()
        {
            var config = new ReverseMarkdown.Config
            {
                UnknownTags = Config.UnknownTagsOption.Drop,
                GithubFlavored = true,
                RemoveComments = true,
                SmartHrefHandling = true
            };
            _markdownConverter = new Converter(config);
        }

        public string Convert(string pageXml, string assetsFolder, string relativeAssetsPath)
        {
            _assetsFolder = assetsFolder;
            _relativeAssetsPath = relativeAssetsPath;
            _imageCounter = 0;

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

            // Convert HTML to Markdown using ReverseMarkdown library
            var markdown = _markdownConverter.Convert(html);

            // Final cleanup
            markdown = CleanupMarkdown(markdown);

            return markdown;
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

        private void ProcessOutline(XElement outline, StringBuilder html)
        {
            var oeChildren = outline.Element(_ns + "OEChildren");
            if (oeChildren != null)
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

        private string ProcessTextElement(XElement t)
        {
            // Get raw text - may be in CDATA or direct
            string rawText = "";
            
            var cdata = t.Nodes().OfType<XCData>().FirstOrDefault();
            if (cdata != null)
            {
                rawText = cdata.Value;
            }
            else
            {
                rawText = t.Value;
            }

            if (string.IsNullOrEmpty(rawText)) return "";

            // Always normalize anchor tags first, regardless of other HTML detection
            // This catches <a\nhref=... patterns where the tag spans lines
            if (rawText.Contains("<a"))
            {
                // Normalize anchor tags - collapse any whitespace after <a to single space
                rawText = Regex.Replace(rawText,
                    @"<a\s+",
                    "<a ",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
            }

            // If text contains HTML (from OneNote's rich text), pass it through
            // ReverseMarkdown will handle the conversion
            if (rawText.Contains("<span") || rawText.Contains("<a ") || rawText.Contains("<b>") || rawText.Contains("<i>"))
            {
                // Clean up OneNote's span styles to simpler HTML
                rawText = ConvertOneNoteStylesToHtml(rawText);
                return rawText;
            }

            // Apply inline styles from T element
            var style = t.Attribute("style")?.Value ?? "";
            
            // Escape plain text for HTML
            var escaped = System.Net.WebUtility.HtmlEncode(rawText);
            
            if (style.Contains("font-weight:bold"))
            {
                escaped = $"<strong>{escaped}</strong>";
            }
            if (style.Contains("font-style:italic"))
            {
                escaped = $"<em>{escaped}</em>";
            }
            if (style.Contains("text-decoration:line-through"))
            {
                escaped = $"<del>{escaped}</del>";
            }

            return escaped;
        }
        private string ConvertOneNoteStylesToHtml(string html)
        {
            // Convert OneNote's inline styles to standard HTML tags
            // font-weight:bold -> <strong>
            html = Regex.Replace(html, 
                @"<span[^>]*style='[^']*font-weight:\s*bold[^']*'[^>]*>([^<]*)</span>",
                "<strong>$1</strong>", RegexOptions.IgnoreCase);
            
            // font-style:italic -> <em>
            html = Regex.Replace(html,
                @"<span[^>]*style='[^']*font-style:\s*italic[^']*'[^>]*>([^<]*)</span>",
                "<em>$1</em>", RegexOptions.IgnoreCase);

            // background:yellow (highlight) -> <mark>
            html = Regex.Replace(html,
                @"<span[^>]*style='[^']*background:\s*yellow[^']*'[^>]*>([^<]*)</span>",
                "<mark>$1</mark>", RegexOptions.IgnoreCase);

            // Remove remaining span tags but keep content
            html = Regex.Replace(html, @"<span[^>]*>", "");
            html = Regex.Replace(html, @"</span>", "");

            // Clean up mso- styles (Microsoft Office specific)
            html = Regex.Replace(html, @"mso-[^;""']+[;]?", "");

            return html;
        }

        private void ProcessImage(XElement image, StringBuilder html)
        {
            html.Append(ProcessImageToHtml(image));
        }

        private string ProcessImageToHtml(XElement image)
        {
            try
            {
                var dataElement = image.Element(_ns + "Data");
                if (dataElement == null || string.IsNullOrWhiteSpace(dataElement.Value))
                {
                    return "<p><em>[Image - no embedded data]</em></p>";
                }

                var base64Data = dataElement.Value.Trim();
                
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

                // Generate unique filename
                _imageCounter++;
                var fileName = $"image_{_imageCounter:D4}{extension}";
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

            var sb = new StringBuilder();
            sb.AppendLine("<table>");

            bool isFirstRow = true;
            foreach (var row in rows)
            {
                sb.AppendLine("<tr>");
                foreach (var cell in row.Elements(_ns + "Cell"))
                {
                    var tag = isFirstRow ? "th" : "td";
                    var cellContent = GetCellContent(cell);
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
                    text.Append(ProcessTextElement(t));
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
                
                // Strip HTML tags
                text = Regex.Replace(text, "<[^>]+>", "");
                sb.Append(text);
            }
            return sb.ToString();
        }

        private string CleanupMarkdown(string markdown)
        {
            // Convert <br> and <br/> tags to proper line breaks FIRST
            // These can appear in tables and regular content
            markdown = Regex.Replace(markdown, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);

            // Aggressively find and convert ALL <a>...</a> tags to Markdown links
            // This regex handles any whitespace/newlines within the tag
            markdown = ConvertAllAnchorTags(markdown);

            // Fix escaped underscores in existing Markdown links [text](url)
            // URLs should not have escaped underscores
            markdown = Regex.Replace(markdown,
                @"\]\(([^)]+)\)",
                match => {
                    var url = match.Groups[1].Value;
                    url = url.Replace("\\_", "_");
                    return $"]({url})";
                });

            // Fix escaped underscores in general text
            // ReverseMarkdown escapes underscores to prevent italic formatting,
            // but this looks wrong in code, variable names, etc.
            // We'll unescape all \_ to _ since OneNote doesn't use markdown formatting
            markdown = markdown.Replace("\\_", "_");

            // Fix escaped asterisks in general text
            // Same reasoning - OneNote content shouldn't have escaped asterisks
            markdown = markdown.Replace("\\*", "*");

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
            
            // Clean up HTML entities that might have slipped through
            markdown = markdown.Replace("&nbsp;", " ");
            markdown = markdown.Replace("&amp;", "&");
            markdown = markdown.Replace("&lt;", "<");
            markdown = markdown.Replace("&gt;", ">");
            markdown = markdown.Replace("&quot;", "\"");
            
            // Remove empty paragraphs
            markdown = Regex.Replace(markdown, @"\n\n\n+", "\n\n");
            
            // Trim lines
            var lines = markdown.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = lines[i].TrimEnd();
            }
            
            return string.Join("\n", lines).Trim();
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
