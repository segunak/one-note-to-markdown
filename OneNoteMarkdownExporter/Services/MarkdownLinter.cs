using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace OneNoteMarkdownExporter.Services
{
    /// <summary>
    /// Built-in Markdown linter that fixes common formatting issues.
    /// Inspired by markdownlint rules but implemented natively in C#.
    /// </summary>
    public class MarkdownLinter
    {
        public LintOptions Options { get; set; }

        public MarkdownLinter()
        {
            Options = LintOptions.CreateDefault();
        }

        public MarkdownLinter(LintOptions options)
        {
            Options = options;
        }

        /// <summary>
        /// Applies all enabled linting rules to the markdown content.
        /// </summary>
        public string Lint(string markdown)
        {
            if (string.IsNullOrEmpty(markdown))
                return markdown;

            // Apply rules in a logical order
            
            // First, handle line-level fixes
            if (Options.MD010_NoHardTabs)
                markdown = FixHardTabs(markdown);

            if (Options.MD009_NoTrailingSpaces)
                markdown = FixTrailingSpaces(markdown);

            // Heading fixes
            if (Options.MD018_NoMissingSpaceAtx)
                markdown = FixMissingSpaceAtx(markdown);

            if (Options.MD019_NoMultipleSpaceAtx)
                markdown = FixMultipleSpaceAtx(markdown);

            if (Options.MD021_NoMultipleSpaceClosedAtx)
                markdown = FixMultipleSpaceClosedAtx(markdown);

            if (Options.MD023_HeadingStartLeft)
                markdown = FixHeadingStartLeft(markdown);

            // Blockquote fixes
            if (Options.MD027_NoMultipleSpaceBlockquote)
                markdown = FixMultipleSpaceBlockquote(markdown);

            // List fixes
            if (Options.MD030_ListMarkerSpace)
                markdown = FixListMarkerSpace(markdown);

            if (Options.MD004_UnorderedListStyle)
                markdown = FixUnorderedListStyle(markdown);

            if (Options.MD007_UnorderedListIndent)
                markdown = FixUnorderedListIndent(markdown);

            if (Options.MD032_ListSurroundedByBlankLines)
                markdown = FixListSurroundedByBlankLines(markdown);

            // Table fixes
            if (Options.MD055_TablePipeStyle)
                markdown = FixTablePipeStyle(markdown);

            if (Options.MD056_TableColumnCount)
                markdown = FixTableColumnCount(markdown);

            // Inline formatting fixes
            if (Options.MD037_NoSpaceInEmphasis)
                markdown = FixSpaceInEmphasis(markdown);

            if (Options.MD038_NoSpaceInCode)
                markdown = FixSpaceInCode(markdown);

            if (Options.MD039_NoSpaceInLinks)
                markdown = FixSpaceInLinks(markdown);

            // URL fixes
            if (Options.MD034_NoBareUrls)
                markdown = FixBareUrls(markdown);

            // Blank line fixes (do this late as other fixes may affect line structure)
            if (Options.MD012_NoMultipleBlanks)
                markdown = FixMultipleBlanks(markdown);

            // Final newline (always do this last)
            if (Options.MD047_SingleTrailingNewline)
                markdown = FixTrailingNewline(markdown);

            return markdown;
        }

        #region Rule Implementations

        /// <summary>
        /// MD009: Remove trailing whitespace from lines
        /// </summary>
        private string FixTrailingSpaces(string markdown)
        {
            var lines = markdown.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = lines[i].TrimEnd(' ', '\t');
            }
            return string.Join("\n", lines);
        }

        /// <summary>
        /// MD010: Replace hard tabs with spaces
        /// </summary>
        private string FixHardTabs(string markdown)
        {
            // Replace tabs with 4 spaces (common convention)
            return markdown.Replace("\t", "    ");
        }

        /// <summary>
        /// MD012: No multiple consecutive blank lines
        /// </summary>
        private string FixMultipleBlanks(string markdown)
        {
            // Replace 3+ consecutive newlines with just 2 (one blank line)
            return Regex.Replace(markdown, @"\n{3,}", "\n\n");
        }

        /// <summary>
        /// MD018: No missing space after hash in atx heading
        /// #Header → # Header
        /// </summary>
        private string FixMissingSpaceAtx(string markdown)
        {
            // Match # not followed by space (but followed by non-# content)
            return Regex.Replace(markdown, 
                @"^(#{1,6})([^\s#])", 
                "$1 $2", 
                RegexOptions.Multiline);
        }

        /// <summary>
        /// MD019: No multiple spaces after hash in atx heading
        /// #  Header → # Header
        /// </summary>
        private string FixMultipleSpaceAtx(string markdown)
        {
            return Regex.Replace(markdown, 
                @"^(#{1,6})[ ]{2,}", 
                "$1 ", 
                RegexOptions.Multiline);
        }

        /// <summary>
        /// MD021: No multiple spaces inside closed atx heading
        /// # Header  # → # Header #
        /// </summary>
        private string FixMultipleSpaceClosedAtx(string markdown)
        {
            return Regex.Replace(markdown,
                @"^(#{1,6}\s+.+?)[ ]{2,}(#{1,6})$",
                "$1 $2",
                RegexOptions.Multiline);
        }

        /// <summary>
        /// MD023: Headings must start at the beginning of the line
        /// </summary>
        private string FixHeadingStartLeft(string markdown)
        {
            return Regex.Replace(markdown, 
                @"^[ \t]+(#{1,6}\s)", 
                "$1", 
                RegexOptions.Multiline);
        }

        /// <summary>
        /// MD027: No multiple spaces after blockquote symbol
        /// >  text → > text
        /// </summary>
        private string FixMultipleSpaceBlockquote(string markdown)
        {
            return Regex.Replace(markdown, 
                @"^(>+)[ ]{2,}", 
                "$1 ", 
                RegexOptions.Multiline);
        }

        /// <summary>
        /// MD030: Correct spaces after list markers
        /// -  item → - item
        /// 1.  item → 1. item
        /// </summary>
        private string FixListMarkerSpace(string markdown)
        {
            // Bullet lists: -, *, +
            markdown = Regex.Replace(markdown, 
                @"^([ \t]*[-*+])[ ]{2,}", 
                "$1 ", 
                RegexOptions.Multiline);

            // Numbered lists: 1., 2., etc.
            markdown = Regex.Replace(markdown, 
                @"^([ \t]*\d+\.)[ ]{2,}", 
                "$1 ", 
                RegexOptions.Multiline);

            return markdown;
        }

        /// <summary>
        /// MD004: Consistent unordered list style (use - for all)
        /// * item → - item
        /// + item → - item
        /// </summary>
        private string FixUnorderedListStyle(string markdown)
        {
            // Convert * and + list markers to - for consistency
            markdown = Regex.Replace(markdown, 
                @"^([ \t]*)\*( )", 
                "$1-$2", 
                RegexOptions.Multiline);
            markdown = Regex.Replace(markdown, 
                @"^([ \t]*)\+( )", 
                "$1-$2", 
                RegexOptions.Multiline);
            return markdown;
        }

        /// <summary>
        /// MD007: Unordered list indentation (2 spaces per level)
        /// Normalizes odd indentation to proper 2-space multiples
        /// </summary>
        private string FixUnorderedListIndent(string markdown)
        {
            var lines = markdown.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var match = Regex.Match(lines[i], @"^([ \t]*)([-*+])( .*)$");
                if (match.Success)
                {
                    var indent = match.Groups[1].Value;
                    var marker = match.Groups[2].Value;
                    var rest = match.Groups[3].Value;
                    
                    // Convert tabs to spaces and count
                    var spaces = indent.Replace("\t", "    ").Length;
                    
                    // Round to nearest 2-space multiple
                    var normalizedSpaces = (spaces / 2) * 2;
                    
                    lines[i] = new string(' ', normalizedSpaces) + marker + rest;
                }
            }
            return string.Join("\n", lines);
        }

        /// <summary>
        /// MD032: Lists should be surrounded by blank lines
        /// </summary>
        private string FixListSurroundedByBlankLines(string markdown)
        {
            var lines = markdown.Split('\n').ToList();
            var result = new List<string>();
            
            for (int i = 0; i < lines.Count; i++)
            {
                bool isListItem = Regex.IsMatch(lines[i], @"^[ \t]*[-*+]\s") || 
                                  Regex.IsMatch(lines[i], @"^[ \t]*\d+\.\s");
                bool prevIsListItem = i > 0 && (Regex.IsMatch(lines[i-1], @"^[ \t]*[-*+]\s") || 
                                               Regex.IsMatch(lines[i-1], @"^[ \t]*\d+\.\s"));
                bool prevIsBlank = i > 0 && string.IsNullOrWhiteSpace(lines[i-1]);
                bool prevExists = i > 0;
                
                // Add blank line before list if needed
                if (isListItem && prevExists && !prevIsListItem && !prevIsBlank)
                {
                    result.Add("");
                }
                
                // Add blank line after list ends if needed
                if (!isListItem && prevIsListItem && !string.IsNullOrWhiteSpace(lines[i]))
                {
                    result.Add("");
                }
                
                result.Add(lines[i]);
            }
            
            return string.Join("\n", result);
        }

        /// <summary>
        /// MD055: Table pipe style - ensure consistent leading/trailing pipes
        /// </summary>
        private string FixTablePipeStyle(string markdown)
        {
            var lines = markdown.Split('\n');
            bool inTable = false;
            
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                
                // Detect table rows (contain | character)
                if (line.Contains('|'))
                {
                    // Check if this looks like a table row
                    bool isTableRow = Regex.IsMatch(line, @"\|.*\|") || 
                                     Regex.IsMatch(line, @"^\|?") && line.Count(c => c == '|') >= 1;
                    
                    if (isTableRow)
                    {
                        inTable = true;
                        // Ensure leading and trailing pipes
                        if (!line.StartsWith("|"))
                            line = "|" + line;
                        if (!line.EndsWith("|"))
                            line = line + "|";
                        lines[i] = line;
                    }
                }
                else if (inTable && string.IsNullOrWhiteSpace(line))
                {
                    inTable = false;
                }
            }
            
            return string.Join("\n", lines);
        }

        /// <summary>
        /// MD056: Table column count - ensure all rows have same number of columns
        /// </summary>
        private string FixTableColumnCount(string markdown)
        {
            var lines = markdown.Split('\n');
            var tableStart = -1;
            var tableEnd = -1;
            int expectedColumns = 0;
            
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                bool isTableRow = line.StartsWith("|") && line.EndsWith("|") && line.Length > 1;
                
                if (isTableRow)
                {
                    if (tableStart == -1)
                    {
                        tableStart = i;
                        expectedColumns = line.Count(c => c == '|') - 1; // pipes - 1 = columns
                    }
                    tableEnd = i;
                    
                    // Count current columns
                    int currentColumns = line.Count(c => c == '|') - 1;
                    
                    // Add missing pipes if needed
                    if (currentColumns < expectedColumns)
                    {
                        var missingPipes = expectedColumns - currentColumns;
                        // Insert before final pipe
                        line = line.Substring(0, line.Length - 1) + 
                               string.Concat(Enumerable.Repeat(" |", missingPipes)) + 
                               "|";
                        lines[i] = line;
                    }
                }
                else if (tableStart != -1 && !isTableRow)
                {
                    // End of table
                    tableStart = -1;
                    expectedColumns = 0;
                }
            }
            
            return string.Join("\n", lines);
        }

        /// <summary>
        /// MD037: No spaces inside emphasis markers
        /// ** bold ** → **bold**
        /// * italic * → *italic*
        /// __ bold __ → __bold__
        /// _ italic _ → _italic_
        /// </summary>
        private string FixSpaceInEmphasis(string markdown)
        {
            // Bold with **
            markdown = Regex.Replace(markdown, @"\*\*\s+(.+?)\s+\*\*", "**$1**");
            // Bold with __
            markdown = Regex.Replace(markdown, @"__\s+(.+?)\s+__", "__$1__");
            // Italic with * (be careful not to match ** patterns)
            markdown = Regex.Replace(markdown, @"(?<!\*)\*\s+(.+?)\s+\*(?!\*)", "*$1*");
            // Italic with _
            markdown = Regex.Replace(markdown, @"(?<!_)_\s+(.+?)\s+_(?!_)", "_$1_");

            return markdown;
        }

        /// <summary>
        /// MD038: No spaces inside code span backticks
        /// ` code ` → `code`
        /// </summary>
        private string FixSpaceInCode(string markdown)
        {
            // Single backtick code spans
            markdown = Regex.Replace(markdown, @"`\s+(.+?)\s+`", "`$1`");
            return markdown;
        }

        /// <summary>
        /// MD039: No spaces inside link text
        /// [ text ](url) → [text](url)
        /// </summary>
        private string FixSpaceInLinks(string markdown)
        {
            markdown = Regex.Replace(markdown, @"\[\s+(.+?)\s+\]\(", "[$1](");
            return markdown;
        }

        /// <summary>
        /// MD034: No bare URLs - wrap them in angle brackets
        /// https://example.com → <https://example.com>
        /// </summary>
        private string FixBareUrls(string markdown)
        {
            // Match URLs that are not already:
            // - Inside angle brackets <url>
            // - Inside markdown links [text](url)
            // - Inside inline code `url`
            // Pattern matches http:// or https:// URLs not preceded by <, (, or `
            markdown = Regex.Replace(markdown,
                @"(?<!<|\(|`)\b(https?://[^\s<>\[\]\)\`]+)",
                "<$1>");
            return markdown;
        }

        /// <summary>
        /// MD047: Files should end with a single newline character
        /// </summary>
        private string FixTrailingNewline(string markdown)
        {
            // Remove all trailing whitespace/newlines, then add exactly one newline
            markdown = markdown.TrimEnd();
            return markdown + "\n";
        }

        #endregion
    }

    /// <summary>
    /// Configuration options for the Markdown linter.
    /// Each option corresponds to a markdownlint rule.
    /// </summary>
    public class LintOptions
    {
        // Whitespace rules
        public bool MD009_NoTrailingSpaces { get; set; } = true;
        public bool MD010_NoHardTabs { get; set; } = true;
        public bool MD012_NoMultipleBlanks { get; set; } = true;

        // Heading rules
        public bool MD018_NoMissingSpaceAtx { get; set; } = true;
        public bool MD019_NoMultipleSpaceAtx { get; set; } = true;
        public bool MD021_NoMultipleSpaceClosedAtx { get; set; } = true;
        public bool MD023_HeadingStartLeft { get; set; } = true;

        // Blockquote rules
        public bool MD027_NoMultipleSpaceBlockquote { get; set; } = true;

        // List rules
        public bool MD030_ListMarkerSpace { get; set; } = true;
        public bool MD004_UnorderedListStyle { get; set; } = true;
        public bool MD007_UnorderedListIndent { get; set; } = true;
        public bool MD032_ListSurroundedByBlankLines { get; set; } = true;

        // Table rules
        public bool MD055_TablePipeStyle { get; set; } = true;
        public bool MD056_TableColumnCount { get; set; } = true;

        // Inline formatting rules
        public bool MD037_NoSpaceInEmphasis { get; set; } = true;
        public bool MD038_NoSpaceInCode { get; set; } = true;
        public bool MD039_NoSpaceInLinks { get; set; } = true;

        // URL rules
        public bool MD034_NoBareUrls { get; set; } = true;

        // File rules
        public bool MD047_SingleTrailingNewline { get; set; } = true;

        /// <summary>
        /// Creates default options with all rules enabled.
        /// </summary>
        public static LintOptions CreateDefault()
        {
            return new LintOptions();
        }

        /// <summary>
        /// Creates minimal options with only whitespace cleanup.
        /// </summary>
        public static LintOptions CreateMinimal()
        {
            return new LintOptions
            {
                MD009_NoTrailingSpaces = true,
                MD010_NoHardTabs = true,
                MD012_NoMultipleBlanks = true,
                MD018_NoMissingSpaceAtx = false,
                MD019_NoMultipleSpaceAtx = false,
                MD021_NoMultipleSpaceClosedAtx = false,
                MD023_HeadingStartLeft = false,
                MD027_NoMultipleSpaceBlockquote = false,
                MD030_ListMarkerSpace = false,
                MD004_UnorderedListStyle = false,
                MD007_UnorderedListIndent = false,
                MD032_ListSurroundedByBlankLines = false,
                MD055_TablePipeStyle = false,
                MD056_TableColumnCount = false,
                MD037_NoSpaceInEmphasis = false,
                MD038_NoSpaceInCode = false,
                MD039_NoSpaceInLinks = false,
                MD034_NoBareUrls = false,
                MD047_SingleTrailingNewline = true
            };
        }

        /// <summary>
        /// Creates options with all rules disabled.
        /// </summary>
        public static LintOptions CreateNone()
        {
            return new LintOptions
            {
                MD009_NoTrailingSpaces = false,
                MD010_NoHardTabs = false,
                MD012_NoMultipleBlanks = false,
                MD018_NoMissingSpaceAtx = false,
                MD019_NoMultipleSpaceAtx = false,
                MD021_NoMultipleSpaceClosedAtx = false,
                MD023_HeadingStartLeft = false,
                MD027_NoMultipleSpaceBlockquote = false,
                MD030_ListMarkerSpace = false,
                MD004_UnorderedListStyle = false,
                MD007_UnorderedListIndent = false,
                MD032_ListSurroundedByBlankLines = false,
                MD055_TablePipeStyle = false,
                MD056_TableColumnCount = false,
                MD037_NoSpaceInEmphasis = false,
                MD038_NoSpaceInCode = false,
                MD039_NoSpaceInLinks = false,
                MD034_NoBareUrls = false,
                MD047_SingleTrailingNewline = false
            };
        }

        /// <summary>
        /// Gets a list of all rule definitions for UI display.
        /// </summary>
        public static List<LintRuleInfo> GetAllRules()
        {
            return new List<LintRuleInfo>
            {
                new LintRuleInfo("MD009", "No trailing spaces", "Remove trailing whitespace from lines", "Whitespace"),
                new LintRuleInfo("MD010", "No hard tabs", "Convert tabs to spaces", "Whitespace"),
                new LintRuleInfo("MD012", "No multiple blanks", "Collapse multiple blank lines into one", "Whitespace"),
                new LintRuleInfo("MD018", "No missing space in heading", "Add space after # in headings", "Headings"),
                new LintRuleInfo("MD019", "No multiple spaces in heading", "Fix multiple spaces after # in headings", "Headings"),
                new LintRuleInfo("MD021", "No multiple spaces in closed heading", "Fix spacing in closed ATX headings", "Headings"),
                new LintRuleInfo("MD023", "Heading start left", "Headings must start at beginning of line", "Headings"),
                new LintRuleInfo("MD027", "No multiple space blockquote", "Fix multiple spaces after > in blockquotes", "Blockquotes"),
                new LintRuleInfo("MD030", "List marker space", "Correct spacing after list markers", "Lists"),
                new LintRuleInfo("MD004", "Unordered list style", "Use consistent list markers (-)", "Lists"),
                new LintRuleInfo("MD007", "Unordered list indent", "Consistent 2-space indentation", "Lists"),
                new LintRuleInfo("MD032", "Lists surrounded by blank lines", "Add blank lines around lists", "Lists"),
                new LintRuleInfo("MD055", "Table pipe style", "Consistent leading/trailing pipes", "Tables"),
                new LintRuleInfo("MD056", "Table column count", "Consistent column count in tables", "Tables"),
                new LintRuleInfo("MD037", "No space in emphasis", "Remove spaces inside **bold** and *italic*", "Formatting"),
                new LintRuleInfo("MD038", "No space in code", "Remove spaces inside `code` spans", "Formatting"),
                new LintRuleInfo("MD039", "No space in links", "Remove spaces inside [link text]", "Formatting"),
                new LintRuleInfo("MD034", "No bare URLs", "Wrap bare URLs in angle brackets", "URLs"),
                new LintRuleInfo("MD047", "Single trailing newline", "Files end with exactly one newline", "Whitespace")
            };
        }

        /// <summary>
        /// Gets the enabled state for a rule by its ID.
        /// </summary>
        public bool IsRuleEnabled(string ruleId)
        {
            return ruleId switch
            {
                "MD009" => MD009_NoTrailingSpaces,
                "MD010" => MD010_NoHardTabs,
                "MD012" => MD012_NoMultipleBlanks,
                "MD018" => MD018_NoMissingSpaceAtx,
                "MD019" => MD019_NoMultipleSpaceAtx,
                "MD021" => MD021_NoMultipleSpaceClosedAtx,
                "MD023" => MD023_HeadingStartLeft,
                "MD027" => MD027_NoMultipleSpaceBlockquote,
                "MD030" => MD030_ListMarkerSpace,
                "MD004" => MD004_UnorderedListStyle,
                "MD007" => MD007_UnorderedListIndent,
                "MD032" => MD032_ListSurroundedByBlankLines,
                "MD055" => MD055_TablePipeStyle,
                "MD056" => MD056_TableColumnCount,
                "MD037" => MD037_NoSpaceInEmphasis,
                "MD038" => MD038_NoSpaceInCode,
                "MD039" => MD039_NoSpaceInLinks,
                "MD034" => MD034_NoBareUrls,
                "MD047" => MD047_SingleTrailingNewline,
                _ => false
            };
        }

        /// <summary>
        /// Sets the enabled state for a rule by its ID.
        /// </summary>
        public void SetRuleEnabled(string ruleId, bool enabled)
        {
            switch (ruleId)
            {
                case "MD009": MD009_NoTrailingSpaces = enabled; break;
                case "MD010": MD010_NoHardTabs = enabled; break;
                case "MD012": MD012_NoMultipleBlanks = enabled; break;
                case "MD018": MD018_NoMissingSpaceAtx = enabled; break;
                case "MD019": MD019_NoMultipleSpaceAtx = enabled; break;
                case "MD021": MD021_NoMultipleSpaceClosedAtx = enabled; break;
                case "MD023": MD023_HeadingStartLeft = enabled; break;
                case "MD027": MD027_NoMultipleSpaceBlockquote = enabled; break;
                case "MD030": MD030_ListMarkerSpace = enabled; break;
                case "MD004": MD004_UnorderedListStyle = enabled; break;
                case "MD007": MD007_UnorderedListIndent = enabled; break;
                case "MD032": MD032_ListSurroundedByBlankLines = enabled; break;
                case "MD055": MD055_TablePipeStyle = enabled; break;
                case "MD056": MD056_TableColumnCount = enabled; break;
                case "MD037": MD037_NoSpaceInEmphasis = enabled; break;
                case "MD038": MD038_NoSpaceInCode = enabled; break;
                case "MD039": MD039_NoSpaceInLinks = enabled; break;
                case "MD034": MD034_NoBareUrls = enabled; break;
                case "MD047": MD047_SingleTrailingNewline = enabled; break;
            }
        }

        /// <summary>
        /// Creates a deep copy of the options.
        /// </summary>
        public LintOptions Clone()
        {
            return new LintOptions
            {
                MD009_NoTrailingSpaces = this.MD009_NoTrailingSpaces,
                MD010_NoHardTabs = this.MD010_NoHardTabs,
                MD012_NoMultipleBlanks = this.MD012_NoMultipleBlanks,
                MD018_NoMissingSpaceAtx = this.MD018_NoMissingSpaceAtx,
                MD019_NoMultipleSpaceAtx = this.MD019_NoMultipleSpaceAtx,
                MD021_NoMultipleSpaceClosedAtx = this.MD021_NoMultipleSpaceClosedAtx,
                MD023_HeadingStartLeft = this.MD023_HeadingStartLeft,
                MD027_NoMultipleSpaceBlockquote = this.MD027_NoMultipleSpaceBlockquote,
                MD030_ListMarkerSpace = this.MD030_ListMarkerSpace,
                MD004_UnorderedListStyle = this.MD004_UnorderedListStyle,
                MD007_UnorderedListIndent = this.MD007_UnorderedListIndent,
                MD032_ListSurroundedByBlankLines = this.MD032_ListSurroundedByBlankLines,
                MD055_TablePipeStyle = this.MD055_TablePipeStyle,
                MD056_TableColumnCount = this.MD056_TableColumnCount,
                MD037_NoSpaceInEmphasis = this.MD037_NoSpaceInEmphasis,
                MD038_NoSpaceInCode = this.MD038_NoSpaceInCode,
                MD039_NoSpaceInLinks = this.MD039_NoSpaceInLinks,
                MD034_NoBareUrls = this.MD034_NoBareUrls,
                MD047_SingleTrailingNewline = this.MD047_SingleTrailingNewline
            };
        }
    }

    /// <summary>
    /// Information about a lint rule for UI display.
    /// </summary>
    public class LintRuleInfo
    {
        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public string Category { get; }

        public LintRuleInfo(string id, string name, string description, string category)
        {
            Id = id;
            Name = name;
            Description = description;
            Category = category;
        }
    }

    /// <summary>
    /// View model for displaying a lint rule with its enabled state in the UI.
    /// </summary>
    public class LintRuleViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isEnabled;

        public LintRuleInfo Rule { get; }
        
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsEnabled)));
                }
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        public LintRuleViewModel(LintRuleInfo rule, bool isEnabled)
        {
            Rule = rule;
            _isEnabled = isEnabled;
        }
    }
}
