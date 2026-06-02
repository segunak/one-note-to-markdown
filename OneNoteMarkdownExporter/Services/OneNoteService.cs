using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Microsoft.Office.Interop.OneNote;
using OneNoteMarkdownExporter.Models;
using System.Runtime.InteropServices;
using System.Linq;

namespace OneNoteMarkdownExporter.Services
{
    public class OneNoteService
    {
        private Microsoft.Office.Interop.OneNote.Application _oneNoteApp;
        private const string OneNoteNamespace = "http://schemas.microsoft.com/office/onenote/2013/onenote";

        public OneNoteService()
        {
            try
            {
                _oneNoteApp = new Microsoft.Office.Interop.OneNote.Application();
            }
            catch (COMException ex)
            {
                throw new Exception("Could not initialize OneNote. Ensure OneNote Desktop is installed and running.", ex);
            }
        }

        public List<OneNoteItem> GetNotebookHierarchy()
        {
            string xml;
            _oneNoteApp.GetHierarchy(null, HierarchyScope.hsPages, out xml);

            return ParseHierarchyXml(xml);
        }

        public static List<OneNoteItem> ParseHierarchyXml(string xml)
        {
            var doc = XDocument.Parse(xml);
            if (doc.Root == null) return new List<OneNoteItem>();

            var ns = doc.Root.Name.Namespace;
            var items = new List<OneNoteItem>();

            foreach (var notebook in doc.Descendants(ns + "Notebook"))
            {
                items.Add(ParseNode(notebook, ns));
            }

            return items;
        }

        private static OneNoteItem ParseNode(XElement element, XNamespace ns)
        {
            var itemType = GetType(element.Name.LocalName);
            var item = new OneNoteItem
            {
                Id = element.Attribute("ID")?.Value ?? "",
                Name = element.Attribute("name")?.Value ?? "Untitled",
                Type = itemType,
                PageLevel = itemType == OneNoteItemType.Page ? ParsePageLevel(element) : 0,
                LastModifiedTime = ParseLastModifiedTime(element)
            };

            var childItems = new List<OneNoteItem>();
            foreach (var child in element.Elements())
            {
                if (child.Name.LocalName == "Section" || child.Name.LocalName == "SectionGroup" || child.Name.LocalName == "Page")
                {
                    childItems.Add(ParseNode(child, ns));
                }
            }

            item.Children = BuildPageHierarchy(childItems);
            return item;
        }

        private static DateTimeOffset? ParseLastModifiedTime(XElement element)
        {
            var raw = element.Attributes()
                .FirstOrDefault(attribute => attribute.Name.LocalName.Equals("lastModifiedTime", StringComparison.OrdinalIgnoreCase))
                ?.Value;

            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            if (DateTimeOffset.TryParse(
                    raw,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AllowWhiteSpaces,
                    out var parsed))
            {
                return parsed;
            }

            return null;
        }

        public static List<OneNoteItem> BuildPageHierarchy(IEnumerable<OneNoteItem> items)
        {
            var result = new List<OneNoteItem>();
            var ancestorsByLevel = new Dictionary<int, OneNoteItem>();

            foreach (var item in items)
            {
                if (item.Type != OneNoteItemType.Page)
                {
                    result.Add(item);
                    ancestorsByLevel.Clear();
                    continue;
                }

                var pageLevel = Math.Max(0, item.PageLevel);
                OneNoteItem? parent = null;

                foreach (var candidateLevel in ancestorsByLevel.Keys.Where(level => level < pageLevel).OrderByDescending(level => level))
                {
                    parent = ancestorsByLevel[candidateLevel];
                    break;
                }

                if (parent == null)
                {
                    result.Add(item);
                }
                else if (!parent.Children.Contains(item))
                {
                    parent.Children.Add(item);
                }

                foreach (var level in ancestorsByLevel.Keys.Where(level => level >= pageLevel).ToList())
                {
                    ancestorsByLevel.Remove(level);
                }

                ancestorsByLevel[pageLevel] = item;
            }

            return result;
        }

        private static int ParsePageLevel(XElement element)
        {
            var pageLevel = element.Attributes()
                .FirstOrDefault(attribute => attribute.Name.LocalName.Equals("pageLevel", StringComparison.OrdinalIgnoreCase))
                ?.Value;

            return int.TryParse(pageLevel, out var value) && value >= 0 ? value : 0;
        }

        private static OneNoteItemType GetType(string localName)
        {
            return localName switch
            {
                "Notebook" => OneNoteItemType.Notebook,
                "SectionGroup" => OneNoteItemType.SectionGroup,
                "Section" => OneNoteItemType.Section,
                "Page" => OneNoteItemType.Page,
                _ => OneNoteItemType.Page
            };
        }

        /// <summary>
        /// Forces OneNote to sync the specified object (notebook, section, or page) with its source.
        /// This is required for cloud-synced notebooks before publishing.
        /// </summary>
        public void SyncHierarchy(string objectId)
        {
            try
            {
                _oneNoteApp.SyncHierarchy(objectId);
            }
            catch
            {
                // Sync may fail for local notebooks - that's OK
            }
        }

        /// <summary>
        /// Navigates to a page to ensure it's loaded in memory.
        /// This can help with cloud-synced content.
        /// </summary>
        public void NavigateToPage(string pageId)
        {
            try
            {
                _oneNoteApp.NavigateTo(pageId, null, false);
                // Give OneNote a moment to load the page
                System.Threading.Thread.Sleep(500);
            }
            catch
            {
                // Navigation may fail - that's OK
            }
        }
        
        public void PublishPage(string pageId, string outputPath)
        {
            // Ensure the directory exists, otherwise OneNote returns 0x80042006 (hrFileDoesNotExist)
            var directory = System.IO.Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            _oneNoteApp.Publish(pageId, outputPath, PublishFormat.pfHTML, "");
        }

        public string GetPageContent(string pageId)
        {
            string xml;
            _oneNoteApp.GetPageContent(pageId, out xml, PageInfo.piAll);
            return xml;
        }

        /// <summary>
        /// Retrieves binary content (such as images or ink) from a page using the callback ID.
        /// This is needed when images are not embedded directly in the page XML but instead
        /// referenced via a callbackID attribute.
        /// </summary>
        /// <param name="pageId">The OneNote ID of the page containing the binary object.</param>
        /// <param name="callbackId">The callback ID of the binary object to retrieve.</param>
        /// <returns>Base64-encoded string of the binary content, or null if retrieval fails.</returns>
        public string? GetBinaryPageContent(string pageId, string callbackId)
        {
            try
            {
                string base64Content;
                _oneNoteApp.GetBinaryPageContent(pageId, callbackId, out base64Content);
                return base64Content;
            }
            catch (Exception)
            {
                return null;
            }
        }
        
        public void UpdatePageContent(string xml)
        {
            _oneNoteApp.UpdatePageContent(xml);
        }

        public bool ExpandCollapsedParagraphs(string pageId)
        {
            try
            {
                string xml;
                _oneNoteApp.GetPageContent(pageId, out xml, PageInfo.piAll);

                var doc = XDocument.Parse(xml);
                if (doc.Root == null) return false;

                var ns = doc.Root.Name.Namespace;

                // Check if ReadOnly
                var isReadOnly = doc.Root.Attribute("isReadOnly")?.Value == "true";
                if (isReadOnly) return false; // Cannot modify

                bool modified = false;
                foreach (var oe in doc.Descendants(ns + "OE"))
                {
                    var collapsed = oe.Attribute("collapsed");
                    if (collapsed != null && collapsed.Value == "true")
                    {
                        collapsed.Remove();
                        modified = true;
                    }
                }

                if (modified)
                {
                    _oneNoteApp.UpdatePageContent(doc.ToString());
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
