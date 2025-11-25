using HtmlAgilityPack;
using ReverseMarkdown;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace OneNoteMarkdownExporter.Services
{
    public class MarkdownConverterService
    {
        private readonly Converter _converter;

        public MarkdownConverterService()
        {
            var config = new Config
            {
                UnknownTags = Config.UnknownTagsOption.PassThrough,
                GithubFlavored = true,
                RemoveComments = true,
                SmartHrefHandling = true
            };
            _converter = new Converter(config);
        }

        public string Convert(string htmlFilePath, string assetsDestFolder, string relativeAssetsPath)
        {
            var htmlDoc = new HtmlAgilityPack.HtmlDocument();
            htmlDoc.Load(htmlFilePath);

            // 1. Clean HTML
            // Remove display:none to reveal collapsed content
            var hiddenNodes = htmlDoc.DocumentNode.SelectNodes("//*[contains(@style, 'display:none') or contains(@style, 'display: none')]");
            if (hiddenNodes != null)
            {
                foreach (var node in hiddenNodes)
                {
                    var style = node.Attributes["style"].Value;
                    style = Regex.Replace(style, @"display:\s*none;?", "", RegexOptions.IgnoreCase);
                    node.Attributes["style"].Value = style;
                }
            }
            
            // Also check for visibility:hidden
             var invisibleNodes = htmlDoc.DocumentNode.SelectNodes("//*[contains(@style, 'visibility:hidden') or contains(@style, 'visibility: hidden')]");
            if (invisibleNodes != null)
            {
                foreach (var node in invisibleNodes)
                {
                    var style = node.Attributes["style"].Value;
                    style = Regex.Replace(style, @"visibility:\s*hidden;?", "", RegexOptions.IgnoreCase);
                    node.Attributes["style"].Value = style;
                }
            }

            // 2. Handle Images
            // OneNote export creates a folder named "{PageName}_files" or similar alongside the HTML.
            var images = htmlDoc.DocumentNode.SelectNodes("//img");
            if (images != null)
            {
                foreach (var img in images)
                {
                    var src = img.GetAttributeValue("src", "");
                    if (string.IsNullOrEmpty(src)) continue;

                    // The src is usually relative to the HTML file, e.g. "PageName_files/image001.png"
                    // We need to decode the URL because it might contain %20 etc.
                    var decodedSrc = System.Net.WebUtility.UrlDecode(src);
                    var htmlDir = Path.GetDirectoryName(htmlFilePath);
                    if (htmlDir == null) continue;

                    var fullSrcPath = Path.Combine(htmlDir, decodedSrc);
                    
                    if (File.Exists(fullSrcPath))
                    {
                        var extension = Path.GetExtension(fullSrcPath);
                        // Create a unique name to avoid collisions in the central assets folder
                        var uniqueFileName = $"{Guid.NewGuid().ToString("N")}{extension}";
                        var destPath = Path.Combine(assetsDestFolder, uniqueFileName);
                        
                        File.Copy(fullSrcPath, destPath, true);
                        
                        // Update src to relative path
                        // Ensure forward slashes for Markdown compatibility
                        var newSrc = $"{relativeAssetsPath}/{uniqueFileName}".Replace("\\", "/");
                        img.SetAttributeValue("src", newSrc);
                    }
                }
            }

            // 3. Convert to Markdown
            var markdown = _converter.Convert(htmlDoc.DocumentNode.OuterHtml);
            
            return markdown;
        }
    }
}
