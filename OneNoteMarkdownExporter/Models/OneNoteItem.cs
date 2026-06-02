using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace OneNoteMarkdownExporter.Models
{
    public enum OneNoteItemType
    {
        Notebook,
        SectionGroup,
        Section,
        Page
    }

    public class OneNoteItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        private bool _isExpanded;

        public event PropertyChangedEventHandler? PropertyChanged;
        
        /// <summary>
        /// Event raised when selection changes on any item. Used to update UI state.
        /// </summary>
        public static event EventHandler? SelectionChanged;

        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public OneNoteItemType Type { get; set; }

        /// <summary>
        /// OneNote page indentation level. Lower values are closer to the section root.
        /// Only meaningful for pages; containers keep the default value.
        /// </summary>
        public int PageLevel { get; set; } = 0;

        /// <summary>
        /// Last-modified timestamp read from the OneNote hierarchy XML
        /// (the <c>lastModifiedTime</c> attribute on the corresponding node).
        /// Null when the attribute is absent or cannot be parsed.
        /// </summary>
        public DateTimeOffset? LastModifiedTime { get; set; }
        
        public bool IsExpanded 
        { 
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
                }
            }
        }
        
        public List<OneNoteItem> Children { get; set; } = new List<OneNoteItem>();
        public string Path { get; set; } = string.Empty; // File system path for export
        
        public bool IsSelected 
        { 
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                    SelectionChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }
    }
}
