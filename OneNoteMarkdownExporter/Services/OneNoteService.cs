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

        private OneNoteItem ParseNode(XElement element, XNamespace ns)
        {
            var item = new OneNoteItem
            {
                Id = element.Attribute("ID")?.Value ?? "",
                Name = element.Attribute("name")?.Value ?? "Untitled",
                Type = GetType(element.Name.LocalName)
            };

            foreach (var child in element.Elements())
            {
                if (child.Name.LocalName == "Section" || child.Name.LocalName == "SectionGroup" || child.Name.LocalName == "Page")
                {
                    item.Children.Add(ParseNode(child, ns));
                }
            }
            return item;
        }

        private OneNoteItemType GetType(string localName)
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
