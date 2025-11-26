# OneNote to Markdown Exporter

A Windows desktop application that exports Microsoft OneNote notebooks, sections, and pages to Markdown format. Built with C#, [WPF](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/), and [COM Interop](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/cominterop). No [Azure App Registration (Service Principals)](https://learn.microsoft.com/en-us/entra/identity-platform/quickstart-register-app), no cloud authentication, no admin consent required. Just you and your notes.

![OneNote to Markdown Exporter Screenshot](docs/screenshot.png)

## Download

Go to [GitHub Releases](https://github.com/segunak/one-note-to-markdown/releases) to download the latest version. Just grab the `.exe`, run it, and you're good to go.

## Requirements

- **Windows 10 or 11**
- **Microsoft OneNote** (the desktop app that comes with Microsoft 365/Office 365, not the old "OneNote for Windows 10" app which [reached end of support in October 2025](https://support.microsoft.com/en-us/office/what-is-happening-to-onenote-for-windows-10-2b453bfe-66bc-4ab2-9118-01e7eb54d2d6))

> **Which OneNote do I have?** If you installed OneNote through Microsoft 365 or Office 365, you have the right one. The desktop app uses COM Interop, which this tool relies on. If you're unsure, open OneNote, go to **File > Account**, and you should see "Microsoft 365" or your Office subscription info. [More details on OneNote versions here](https://support.microsoft.com/en-us/office/what-s-the-difference-between-the-onenote-versions-a624e692-b78b-4c09-b07f-46181958118f).

## Features

- **Tree view selection** - Pick entire notebooks, specific sections, or individual pages
- **Clean Markdown output** - Proper formatting, no leftover HTML tags
- **Image extraction** - Embedded images saved to an `assets` folder with relative paths
- **Sync-friendly** - "Overwrite existing files" option keeps exports in sync with your notes
- **Built-in Markdown linting** - Automatic cleanup with configurable rules
- **Bundled markdownlint-cli** - Full 50+ rule linting experience, no installation required

## Usage

1. **Launch the app** - OneNote will open automatically if it's not running
2. **Select your content** - Check the boxes next to notebooks, sections, or pages
3. **Choose an output directory** - Defaults to `Downloads\OneNoteExport`
4. **Configure options**:
   - **Overwrite existing files** - Enable this for ongoing syncing
   - **Apply Markdown formatting rules** - Cleans up the output
   - Pick **Built-in linter** or **markdownlint-cli**
5. **Click Start Export**

## Markdown Linting

The app includes two linting options. Both work out of the box with no additional setup.

### Built-in Linter

A custom C# implementation covering the most common Markdown formatting rules:

| Rule | Description |
|------|-------------|
| MD004 | Consistent unordered list style (asterisks, dashes, etc.) |
| MD007 | Proper list indentation |
| MD009 | No trailing whitespace |
| MD010 | No hard tabs (converts to spaces) |
| MD012 | No multiple consecutive blank lines |
| MD018/19/21/23 | Heading formatting consistency |
| MD027 | No multiple spaces after blockquote symbol |
| MD030 | List marker spacing |
| MD032 | Blank lines around lists |
| MD034 | No bare URLs |
| MD037/38/39 | No spaces inside emphasis/code markers |
| MD047 | Files end with a newline |
| MD055/56 | Table cell padding and alignment |

### markdownlint-cli

The full [markdownlint-cli](https://github.com/DavidAnson/markdownlint-cli) experience with 50+ rules. The app bundles Node.js and all dependencies, so just select it and it works.

#### markdownlint-cli Configuration

The `.markdownlint.json` file controls which rules are applied:

```json
{
  "default": true,
  "MD013": false,
  "MD033": false,
  "MD028": false,
  "MD012": false,
  "MD040": false,
  "MD024": false,
  "MD018": false,
  "MD036": false,
  "MD049": false,
  "MD041": false
}
```

| Rule | What It Does | Why It's Disabled |
|------|--------------|-------------------|
| **MD013** | Line length limit (80 chars) | OneNote content doesn't follow line limits |
| **MD033** | No inline HTML | Some exported content may have intentional HTML |
| **MD041** | First line should be H1 | Not all notes start with a heading |
| **MD024** | No duplicate headings | Notes often reuse section headers |
| **MD028** | Blank line inside blockquote | Common in formatted quotes |
| **MD012** | Multiple blank lines | OneNote spacing doesn't always translate cleanly |
| **MD040** | Fenced code blocks need language | Not all code blocks have a language |
| **MD018** | No space after hash in heading | Edge cases in conversion |
| **MD036** | Emphasis instead of heading | Style choice |
| **MD049** | Consistent emphasis style | Mixed styles in source content |

## Troubleshooting

### "OneNote is not installed" error

Make sure you have the full OneNote Desktop application installed, not just "OneNote for Windows 10" from the Microsoft Store.

### Export produces empty files

Check that OneNote is synced and the pages have content. The `GetPageContent()` method only sees what's locally cached.

### Images not exporting

Ensure the pages are fully synced in OneNote. Embedded images need to be downloaded before they can be extracted.

## Building from Source

```powershell
# Clone the repository
git clone https://github.com/segunak/one-note-to-markdown.git
cd one-note-to-markdown

# Build
dotnet build OneNoteMarkdownExporter

# Run
dotnet run --project OneNoteMarkdownExporter
```

Or just open the `OneNoteMarkdownExporter.slnx` file in Visual Studio and click the play button to run the project.

## Technical Details

### How It Works

1. **Connect to OneNote** via COM Interop (`Microsoft.Office.Interop.OneNote`)
2. **Enumerate hierarchy** using `GetHierarchy()` to build the notebook/section/page tree
3. **Export pages** using `GetPageContent()` which returns raw XML with embedded images
4. **Parse XML** to extract text, formatting, and base64-encoded images
5. **Convert to Markdown** using a combination of custom parsing and [ReverseMarkdown](https://github.com/mysticmind/ReverseMarkdown)
6. **Apply linting** to clean up formatting inconsistencies
7. **Save files** with proper folder structure mirroring your notebook organization

## Why I Built This

I hate OneNote. I have only ever used it in cases where I was grandfathered into it. Meaning, the program I was in at school, or the team I was on at work, already used it, so I had to play along. The day I learned about Markdown (shout out to the team [Farm Credit Services of America](https://www.fcsamerica.com/), my first internship), I resolved to do everything I could to never touch OneNote or similar "vendor lock-in" proprietary note taking tools again.

That decision, given the rise of AI and how easily it works with and prefers Markdown, has never looked better. I had some legacy OneNotes I inherited at work that were chock full of domain knowledge, truly rich content, scattered across sections and pages and impossible to easily parse through. To enable [Retrieval Augmented Generation](https://en.wikipedia.org/wiki/Retrieval-augmented_generation) over that information, I wanted to export it to Markdown. I tried all sorts of solutions and hit roadblock after roadblock.

### Other Solutions I Tried

1. **[ConvertOneNote2MarkDown](https://github.com/theohbrothers/ConvertOneNote2MarkDown):** Uses OneNote's `Publish()` method to export pages as Word documents, then converts to Markdown via Pandoc. Doesn't work when Data Loss Prevention policies are enabled.

2. **[ConvertOneNote2MarkDown](https://github.com/SjoerdV/ConvertOneNote2MarkDown):** Another fork of the same approach. Same `Publish()` dependency, same Data Loss Prevention wall.

3. **[onenote_to_markdown](https://github.com/Ben-Gillman/onenote_to_markdown):** Uses the Microsoft Graph API, which requires an Azure App Registration. I'm blocked from doing that at work without a formal justification, and "I want to export some OneNotes to Markdown" doesn't cut it.

4. **[OneNote Export Gist](https://gist.github.com/heardk/ded40b72056cee33abb18f3724e0a580):** Python script that also relies on the Graph API. Same admin consent wall.

5. **[onenote-md-exporter](https://github.com/alxnbl/onenote-md-exporter):** A .NET tool that uses `Publish()` to export pages as HTML. Blocked by Data Loss Prevention policies.

6. **[freeing-onenote](https://github.com/nyanhp/freeing-onenote):** PowerShell module that uses `Publish()`. Same issue.

7. **[Obsidian Importer](https://github.com/obsidianmd/obsidian-importer):** Built into Obsidian, but uses the Graph API under the hood. Same admin consent requirement.

### This Solution

Instead of using `Publish()` (which exports pages as HTML/Word/PDF), use `GetPageContent()`. This method returns the raw XML of a OneNote page, including base64-encoded images. No intermediate file writing.

```csharp
// This gets blocked by Data Loss Prevention policies
onenote.Publish(pageId, tempFile, PublishFormat.pfOneNote, string.Empty);

// This works perfectly, even with sensitivity labels
onenote.GetPageContent(pageId, out string xml, PageInfo.piAll);
```

That's the core insight this app is built on.
