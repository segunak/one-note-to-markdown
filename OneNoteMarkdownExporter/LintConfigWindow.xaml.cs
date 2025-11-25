using System.Collections.ObjectModel;
using System.Windows;
using OneNoteMarkdownExporter.Services;

namespace OneNoteMarkdownExporter;

public partial class LintConfigWindow : Window
{
    public LintOptions Options { get; private set; }
    public bool WasSaved { get; private set; }

    private ObservableCollection<LintRuleViewModel> _rules = new();

    public LintConfigWindow(LintOptions currentOptions)
    {
        InitializeComponent();
        Options = currentOptions.Clone();
        InitializeRules();
        RulesListBox.ItemsSource = _rules;
    }

    private void InitializeRules()
    {
        _rules.Add(new LintRuleViewModel(
            new LintRuleInfo("MD009", "No Trailing Spaces", 
                "Removes trailing whitespace from the end of lines.", "Whitespace"),
            Options.MD009_NoTrailingSpaces));

        _rules.Add(new LintRuleViewModel(
            new LintRuleInfo("MD010", "No Hard Tabs", 
                "Converts hard tab characters to spaces (4 spaces per tab).", "Whitespace"),
            Options.MD010_NoHardTabs));

        _rules.Add(new LintRuleViewModel(
            new LintRuleInfo("MD012", "No Multiple Blank Lines", 
                "Reduces consecutive blank lines to a single blank line.", "Whitespace"),
            Options.MD012_NoMultipleBlanks));

        _rules.Add(new LintRuleViewModel(
            new LintRuleInfo("MD018", "Space After Heading Hash", 
                "Ensures a space exists after # in ATX-style headings.", "Headings"),
            Options.MD018_NoMissingSpaceAtx));

        _rules.Add(new LintRuleViewModel(
            new LintRuleInfo("MD019", "No Multiple Spaces After Hash", 
                "Reduces multiple spaces after heading # to a single space.", "Headings"),
            Options.MD019_NoMultipleSpaceAtx));

        _rules.Add(new LintRuleViewModel(
            new LintRuleInfo("MD021", "No Multiple Spaces in Closed Heading",
                "Fixes spacing in closed ATX headings (# Heading #).", "Headings"),
            Options.MD021_NoMultipleSpaceClosedAtx));

        _rules.Add(new LintRuleViewModel(
            new LintRuleInfo("MD023", "Heading at Start of Line", 
                "Removes leading whitespace from heading lines.", "Headings"),
            Options.MD023_HeadingStartLeft));

        _rules.Add(new LintRuleViewModel(
            new LintRuleInfo("MD027", "Blockquote Spacing", 
                "Reduces multiple spaces after > in blockquotes to a single space.", "Blockquotes"),
            Options.MD027_NoMultipleSpaceBlockquote));

        _rules.Add(new LintRuleViewModel(
            new LintRuleInfo("MD030", "List Marker Spacing", 
                "Ensures consistent spacing after list markers (-, *, numbers).", "Lists"),
            Options.MD030_ListMarkerSpace));

        _rules.Add(new LintRuleViewModel(
            new LintRuleInfo("MD037", "No Space in Emphasis", 
                "Removes spaces inside emphasis markers (*text* or **text**).", "Formatting"),
            Options.MD037_NoSpaceInEmphasis));

        _rules.Add(new LintRuleViewModel(
            new LintRuleInfo("MD038", "No Space in Inline Code", 
                "Removes spaces at the start/end inside backtick code spans.", "Formatting"),
            Options.MD038_NoSpaceInCode));

        _rules.Add(new LintRuleViewModel(
            new LintRuleInfo("MD039", "No Space in Links",
                "Removes spaces inside [link text].", "Formatting"),
            Options.MD039_NoSpaceInLinks));

        _rules.Add(new LintRuleViewModel(
            new LintRuleInfo("MD034", "No Bare URLs",
                "Wraps bare URLs in angle brackets (https://... → <https://...>).", "URLs"),
            Options.MD034_NoBareUrls));

        _rules.Add(new LintRuleViewModel(
            new LintRuleInfo("MD047", "Single Trailing Newline", 
                "Ensures the file ends with exactly one newline character.", "Document"),
            Options.MD047_SingleTrailingNewline));
    }

    private void ApplyRulesToOptions()
    {
        Options.MD009_NoTrailingSpaces = _rules[0].IsEnabled;
        Options.MD010_NoHardTabs = _rules[1].IsEnabled;
        Options.MD012_NoMultipleBlanks = _rules[2].IsEnabled;
        Options.MD018_NoMissingSpaceAtx = _rules[3].IsEnabled;
        Options.MD019_NoMultipleSpaceAtx = _rules[4].IsEnabled;
        Options.MD021_NoMultipleSpaceClosedAtx = _rules[5].IsEnabled;
        Options.MD023_HeadingStartLeft = _rules[6].IsEnabled;
        Options.MD027_NoMultipleSpaceBlockquote = _rules[7].IsEnabled;
        Options.MD030_ListMarkerSpace = _rules[8].IsEnabled;
        Options.MD037_NoSpaceInEmphasis = _rules[9].IsEnabled;
        Options.MD038_NoSpaceInCode = _rules[10].IsEnabled;
        Options.MD039_NoSpaceInLinks = _rules[11].IsEnabled;
        Options.MD034_NoBareUrls = _rules[12].IsEnabled;
        Options.MD047_SingleTrailingNewline = _rules[13].IsEnabled;
    }

    private void EnableAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var rule in _rules)
            rule.IsEnabled = true;
    }

    private void DisableAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var rule in _rules)
            rule.IsEnabled = false;
    }

    private void Minimal_Click(object sender, RoutedEventArgs e)
    {
        // Only whitespace rules
        _rules[0].IsEnabled = true;  // MD009 - Trailing spaces
        _rules[1].IsEnabled = true;  // MD010 - Hard tabs
        _rules[2].IsEnabled = true;  // MD012 - Multiple blank lines
        _rules[3].IsEnabled = false; // MD018
        _rules[4].IsEnabled = false; // MD019
        _rules[5].IsEnabled = false; // MD021
        _rules[6].IsEnabled = false; // MD023
        _rules[7].IsEnabled = false; // MD027
        _rules[8].IsEnabled = false; // MD030
        _rules[9].IsEnabled = false; // MD037
        _rules[10].IsEnabled = false; // MD038
        _rules[11].IsEnabled = false; // MD039
        _rules[12].IsEnabled = false; // MD034
        _rules[13].IsEnabled = true; // MD047 - Trailing newline
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        WasSaved = false;
        DialogResult = false;
        Close();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ApplyRulesToOptions();
        WasSaved = true;
        DialogResult = true;
        Close();
    }
}
