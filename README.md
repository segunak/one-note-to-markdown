# OneNote to Markdown Exporter

A Windows desktop application that exports Microsoft OneNote notebooks, sections, and pages to Markdown format. Built with C#, [WPF](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/), and [COM Interop](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/cominterop). No [Azure App Registration (Service Principals)](https://learn.microsoft.com/en-us/entra/identity-platform/quickstart-register-app), no cloud authentication, no admin consent required. Just you and your notes.

![OneNote to Markdown Exporter Screenshot](docs/screenshot.png)

## Buy Me a Coffee

This is all [free](https://www.biblegateway.com/passage/?search=Matthew%2010%3A8&version=NIV), but if you're feeling generous, you can [buy me a coffee](https://buymeacoffee.com/segunak), or herbal tea, which is more my thing!

<a href="https://buymeacoffee.com/segunak" target="_blank"><img src="https://cdn.buymeacoffee.com/buttons/v2/default-yellow.png" alt="Buy Me a Coffee" style="height: 72px !important; width: 260px !important;"></a>

## Download

Go to [GitHub Releases](https://github.com/segunak/one-note-to-markdown/releases) to download the latest version.

1. Download the `.zip` file from the latest release
2. Extract the folder (it contains the `.exe` and a `resources` folder)
3. Run `OneNoteMarkdownExporter.exe`

> **Important:** Keep the `resources` folder in the same directory as the `.exe`. It contains stuff needed for Markdown linting.

## Requirements

- **Windows 10 or 11**
- **Microsoft OneNote** (the desktop app that comes with Microsoft 365/Office 365, not the old "OneNote for Windows 10" app which [reached end of support in October 2025](https://support.microsoft.com/en-us/office/what-is-happening-to-onenote-for-windows-10-2b453bfe-66bc-4ab2-9118-01e7eb54d2d6))

> **Which OneNote do I have?** If you installed OneNote through Microsoft 365 or Office 365, you have the right one. The desktop app uses COM Interop, which this tool relies on. If you're unsure, open OneNote, go to **File > Account**, and you should see "Microsoft 365" or your Office subscription info. [More details on OneNote versions here](https://support.microsoft.com/en-us/office/what-s-the-difference-between-the-onenote-versions-a624e692-b78b-4c09-b07f-46181958118f).

## Features

- **Two ways to run** - GUI for interactive use, CLI for scripting and automation
- **Tree view selection** - Pick entire notebooks, specific sections, or individual pages
- **Subpage hierarchy** - OneNote subpages export into nested folders
- **Clean Markdown output** - Proper formatting, no leftover HTML tags
- **Image extraction** - Embedded images saved to a configurable assets folder with relative paths
- **Asset organization modes** - Store assets centrally, per notebook, per section, or per page
- **Date preservation** - Exported Markdown files keep OneNote created and modified timestamps
- **Sync-friendly** - "Overwrite existing files" option keeps exports in sync with your notes
- **Markdown linting** - Automatic cleanup via bundled markdownlint-cli2 (configurable)

## Usage

This tool supports both **GUI mode** and **CLI mode**:

| Mode | How to Launch | Best For |
|------|---------------|----------|
| **GUI** | Double-click the `.exe` or run without arguments | Interactive use, exploring notebooks, one-time exports |
| **CLI** | Run with command-line arguments | Scripting, automation, scheduled tasks, AI tool integration |

The app automatically detects which mode to use based on whether you pass command-line arguments.

## GUI Mode

Double-click `OneNoteMarkdownExporter.exe` to launch the graphical interface.

### Steps

1. **Launch the app** - OneNote will open automatically if it's not running
2. **Select your content** - Check the boxes next to notebooks, sections, or pages
3. **Choose an output directory** - Defaults to `Downloads\OneNoteExport`
4. **Choose asset organization** - Defaults to one centralized assets folder
5. **Choose an assets folder** - Available in centralized mode and defaults to `<output>\_assets`
6. **Configure options**:
   - **Overwrite existing files** - Enable this for ongoing syncing
   - **Apply Markdown linting** - Cleans up the output (can be toggled off)
   - **Preserve OneNote dates as file timestamps** - Sets exported `.md` Created and Modified dates from OneNote
   - **Add YAML front matter metadata** - Optional; changes Markdown content when enabled
7. **Click Start Export**

## CLI Mode

Run with command-line arguments for scripting, automation, scheduled tasks, etc. The app runs headlessly without opening the GUI.

```powershell
# Export all notebooks
OneNoteMarkdownExporter.exe --all

# Export a specific notebook
OneNoteMarkdownExporter.exe --notebook "Work Notes"

# Export to a custom directory
OneNoteMarkdownExporter.exe --all --output "C:\MyExports"

# Export images/assets to a custom folder
OneNoteMarkdownExporter.exe --all --assets-folder "D:\OneNoteAssets"

# Export with assets grouped per page
OneNoteMarkdownExporter.exe --all --asset-organization page

# Show help
OneNoteMarkdownExporter.exe --help
```

### CLI Parameters

#### Selection (at least one required)

| Option | Description |
|--------|-------------|
| `--all` | Export all notebooks |
| `--notebook <name>` | Export specific notebook by name |
| `--section <path>` | Export section by path, e.g., `"Notebook/Section"` |
| `--page <id>` | Export page by OneNote ID |

#### Output

| Option | Description |
|--------|-------------|
| `--output`, `-o` `<path>` | Output directory (default: `Downloads\OneNoteExport`) |
| `--assets-folder <path>` | Folder for exported images/assets (default: `<output>\_assets`) |
| `--asset-organization <mode>` | Asset organization mode: `centralized`, `notebook`, `section`, or `page` |
| `--overwrite` | Overwrite existing files instead of creating numbered copies |

#### Linting

| Option | Description |
|--------|-------------|
| `--no-lint` | Disable Markdown linting (markdownlint-cli2) |
| `--lint-config <path>` | Path to custom `.markdownlint.json` configuration file |

#### Dates

| Option | Description |
|--------|-------------|
| `--no-preserve-dates` | Do not set exported `.md` file timestamps from OneNote dates |
| `--date-metadata <mode>` | Date metadata mode: `none` or `yaml` (default: `none`) |

#### Utility

| Option | Description |
|--------|-------------|
| `--list` | List all notebooks, sections, and pages (no export) |
| `--dry-run` | Preview what would be exported without creating files |
| `--verbose`, `-v` | Show detailed output including file paths |
| `--quiet`, `-q` | Show only errors (suppress progress messages) |
| `--help`, `-h` | Show help and usage information |

### CLI Examples

```powershell
# List all notebooks and their structure
OneNoteMarkdownExporter.exe --list

# List with verbose mode to see page IDs
OneNoteMarkdownExporter.exe --list --verbose

# Preview what would be exported (no files created)
OneNoteMarkdownExporter.exe --notebook "Personal" --dry-run

# Export multiple notebooks
OneNoteMarkdownExporter.exe --notebook "Work" --notebook "Personal"

# Export a specific section within a notebook
OneNoteMarkdownExporter.exe --section "Work Notes/Meeting Notes"

# Export everything, overwrite existing, skip linting
OneNoteMarkdownExporter.exe --all --overwrite --no-lint

# Export without preserving OneNote dates as file timestamps
OneNoteMarkdownExporter.exe --all --no-preserve-dates

# Export with YAML front matter metadata
OneNoteMarkdownExporter.exe --all --date-metadata yaml

# Quiet mode for scheduled tasks (only shows errors)
OneNoteMarkdownExporter.exe --all --quiet --overwrite

# Full verbose export to custom location
OneNoteMarkdownExporter.exe --all --output "D:\Backups\OneNote" --verbose --overwrite

# Export notes and store assets in a separate folder
OneNoteMarkdownExporter.exe --all --output "D:\Backups\OneNote" --assets-folder "D:\Backups\OneNoteAssets"

# Export notes with assets grouped under each notebook folder
OneNoteMarkdownExporter.exe --all --asset-organization notebook

# Export notes with page-local asset folders
OneNoteMarkdownExporter.exe --all --asset-organization page
```

### Assets Folder

Exported images are saved to `<output>\_assets` by default. This is the `centralized` asset organization mode.

Use the GUI assets folder field or the CLI `--assets-folder <path>` option to choose a different centralized folder. Relative paths are resolved from the output directory, and absolute paths are used as provided. Missing folders are created automatically. Existing asset folders are reused, and generated asset files with the same names are overwritten on later exports. Paths where the assets folder itself would be an existing file are rejected. Markdown image links are generated relative to each exported page.

Generated assets folders are created only when exported content actually contains assets.

Use `--asset-organization <mode>` or the GUI asset organization selector to choose a different layout:

| Mode | Asset folder layout | Custom assets folder |
|------|---------------------|----------------------|
| `centralized` | `<output>\_assets` or your chosen folder | Yes |
| `notebook` | Each notebook folder gets `_assets_NotebookName` | No |
| `section` | Each section folder gets `_assets_SectionName` | No |
| `page` | Each page gets `_assets_PageName` beside the Markdown file | No |

Generated scoped folder names use a Windows-safe PascalCase suffix with spaces and punctuation removed. For example, `Project Notes` becomes `_assets_ProjectNotes`, and `Q&A / Work` becomes `_assets_QAWork`. Apostrophes are removed without splitting the word, so `Segun's Notebook` becomes `_assets_SegunsNotebook`. If two generated names collide in the same folder, the second name receives a stable hash suffix such as `_assets_ProjectNotes_a1b2c3d4`.

### Date Preservation

By default, exported Markdown page files preserve OneNote page dates as Windows file timestamps. The exported `.md` file creation time is set from the OneNote created date when available, and the file modified time is set from the OneNote last modified date when available. Timestamps are applied after Markdown conversion, optional YAML metadata, linting, and file writing.

Date preservation does not change Markdown content. Use the GUI checkbox or `--no-preserve-dates` to turn it off.

YAML front matter metadata is off by default because it changes Markdown content. Enable it with the GUI checkbox or `--date-metadata yaml` when you want metadata inside each Markdown file.

```yaml
---
created: "2024-01-15 10:30 UTC"
updated: "2024-02-20 14:45 UTC"
---
```

### Pages, Subpages, And Sub-subpages

A OneNote page exports as a Markdown file. When that page has subpages, the exporter also creates a matching folder with the same name. The Markdown file is the page itself. The matching folder is the expanded subpage area under that page.

For a page with one subpage, the layout looks like this:

```text
Section\
  Strategic Vision.md
  Strategic Vision\
    Subpage for Testing.md
  Goals.md
  Ideas.md
```

`Strategic Vision.md` is the OneNote page. `Strategic Vision\` is the matching folder that contains subpages of that page. `Goals.md` and `Ideas.md` remain peer pages in the same section.

The same pattern repeats for sub-subpages:

```text
Section\
  Parent Page.md
  Parent Page\
    Child Page.md
    Child Page\
      Grandchild Page.md
```

`Child Page.md` is the subpage itself, and `Child Page\` contains that subpage's children.

If only a subpage is selected for export, the parent folder is still created so the exported page keeps its OneNote context, but the unselected parent page Markdown is not exported.

### Windows Safe Names

Exported folder and file names are made safe for Windows. Invalid filename characters are replaced, trailing spaces and periods are removed, and reserved Windows names such as `CON`, `NUL`, `COM1`, and `LPT1` are adjusted. Long names are preserved when the full target path fits within the standard Windows path budget. When a generated path is too long, only the generated OneNote-derived name is shortened, and a stable hash suffix keeps repeated exports targeting the same file.

Folder names also reserve space for nested sections, subpages, and the selected asset layout. These budgets use the loaded hierarchy, so selecting fewer pages does not change how their ancestor folder names are shortened. Compact names retain the full eight-character hash, and shortened image names and numbered copies remain distinct. If the output or centralized assets path leaves too little room even for compact names, the export stops before writing pages and asks you to choose a shorter folder.

## Markdown Linting

The app uses [markdownlint-cli2](https://github.com/DavidAnson/markdownlint-cli2) for Markdown linting. Node.js and the CLI bundle are included, so it works out of the box with no additional setup.

Conversion preserves literal brackets, Markdown-like text, and typed separator lines instead of interpreting them as links or formatting. Actual OneNote hyperlinks, formatting, and code remain functional. Necessary backslashes appear in Markdown source but not in the rendered text. These protections also apply when linting is disabled.

The page title, or the first actual H1 when there is no title, remains the only H1. Later H1 sections and their subordinate headings move down one level, capped at H6, without dropping their text. Tables preserve literal pipes and pad shorter rows to the widest source row. Inline code containing pipes uses safe HTML within table cells.

- **Enabled by default** - Can be toggled off in the UI or with `--no-lint` in CLI
- **Non-blocking** - If linting fails, the error is logged and export continues with the unlinted content
- **Configurable** - Edit the bundled `.markdownlint.json`, or pass another `.json` file with `--lint-config`

### Configuration

Click "Edit .markdownlint.json..." in the UI or find the file in the `resources` folder. The default configuration:

Only JSON rule configuration is supported. YAML, TOML, JavaScript configuration, plugins, and custom rules are not supported.

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
  "MD059": false,
  "MD041": false,
  "MD060": { "style": "compact" }
}
```

MD060 uses compact table spacing so the bundled linter can automatically fix inconsistent padding without requiring aligned columns.

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
| **MD059** | Link text should be descriptive | The exporter preserves the user's wording instead of judging prose |

## Technical Details

### How It Works

1. **Connect to OneNote** via COM Interop (`Microsoft.Office.Interop.OneNote`)
2. **Enumerate hierarchy** using [`GetHierarchy()`](https://learn.microsoft.com/en-us/office/client-developer/onenote/application-interface-onenote#gethierarchy-method) to build the notebook/section/page tree
3. **Export pages** using [`GetPageContent()`](https://learn.microsoft.com/en-us/office/client-developer/onenote/application-interface-onenote#getpagecontent-method) which returns raw XML with embedded images
4. **Parse XML** to extract text, formatting, and base64-encoded images
5. **Convert to Markdown** using a combination of custom parsing and [ReverseMarkdown](https://github.com/mysticmind/ReverseMarkdown)
6. **Apply linting** to clean up formatting inconsistencies
7. **Save files** with proper folder structure mirroring your notebook organization

## Why I Built This

I hate OneNote. I've only ever used it in cases where I was grandfathered into it. Meaning, the program I was in at school, or the team I was on at work, already used it, so I had to play along. The day I learned about Markdown (shout out to the team at [Farm Credit Services of America](https://www.fcsamerica.com/), my first internship that taught me real world software development, and a love of markdown), I resolved to do everything I could to never touch OneNote or similar "vendor lock-in" proprietary note taking tools again.

That decision, given the rise of AI and how easily it works with and prefers Markdown, has never looked better. I had some legacy OneNotes I inherited at work that were chock full of domain knowledge scattered across sections and pages and impossible to easily parse through. To enable [Retrieval Augmented Generation](https://en.wikipedia.org/wiki/Retrieval-augmented_generation) over that information, I wanted to export it to Markdown. I tried all sorts of solutions and hit roadblock after roadblock.

### Other Solutions

1. **[ConvertOneNote2MarkDown](https://github.com/theohbrothers/ConvertOneNote2MarkDown):** PowerShell script that uses OneNote's [`Publish()`](https://learn.microsoft.com/en-us/office/client-developer/onenote/application-interface-onenote#publish-method) method to export pages as Word documents (.docx), then converts them to Markdown using Pandoc. Doesn't work when [Data Loss Prevention](https://learn.microsoft.com/en-us/purview/dlp-learn-about-dlp) policies are enabled because [`Publish()`](https://learn.microsoft.com/en-us/office/client-developer/onenote/application-interface-onenote#publish-method) writes files to disk. Something about DLP blows up any attempt using [`Publish()`](https://learn.microsoft.com/en-us/office/client-developer/onenote/application-interface-onenote#publish-method) to save a file thereafter.

2. **[ConvertOneNote2MarkDown](https://github.com/SjoerdV/ConvertOneNote2MarkDown):** The original version of the above. Same [`Publish()`](https://learn.microsoft.com/en-us/office/client-developer/onenote/application-interface-onenote#publish-method) to Word then Pandoc approach, same [Data Loss Prevention](https://learn.microsoft.com/en-us/purview/dlp-learn-about-dlp) issues.

3. **[onenote_to_markdown](https://github.com/Ben-Gillman/onenote_to_markdown):** A Python script that converts manually copy-pasted text from OneNote into Markdown. Requires you to manually select and copy all notes, save them as text files, then run the script. Not automated and loses formatting/images.

4. **[OneNote Export Gist](https://gist.github.com/heardk/ded40b72056cee33abb18f3724e0a580):** A manual workflow where you export pages to .docx using OneNote's File > Export menu, then use Pandoc commands to convert to Markdown. Not automated, requires manual export of each page.

5. **[onenote-md-exporter](https://github.com/alxnbl/onenote-md-exporter):** A .NET console app that uses [`Publish()`](https://learn.microsoft.com/en-us/office/client-developer/onenote/application-interface-onenote#publish-method) to export pages as Word documents, then converts them to Markdown using Pandoc. Well-built tool with good features, but blocked by [Data Loss Prevention](https://learn.microsoft.com/en-us/purview/dlp-learn-about-dlp) policies because `Publish()` writes intermediate files to disk.

6. **[freeing-onenote](https://github.com/nyanhp/freeing-onenote):** PowerShell script that uses the Microsoft Graph API to retrieve page content and convert to Markdown. Requires an Azure App Registration with appropriate permissions, which is doable, but in some organizations requires admin approval when you're just trying to export your personal notebook.

7. **[Obsidian Importer](https://help.obsidian.md/import/onenote):** Built into Obsidian, but uses the Graph API under the hood. Same admin consent requirement.

### This Solution

Instead of using [`Publish()`](https://learn.microsoft.com/en-us/office/client-developer/onenote/application-interface-onenote#publish-method) (which exports pages to various non-markdown formats that you then convert to markdown), use [`GetPageContent()`](https://learn.microsoft.com/en-us/office/client-developer/onenote/application-interface-onenote#getpagecontent-method). This method returns the raw XML of a OneNote page, including base64-encoded images. No intermediate file writing.

```csharp
// This gets blocked by Data Loss Prevention (DLP) policies
onenote.Publish(pageId, tempFile, PublishFormat.pfOneNote, string.Empty);

// This works, even with sensitivity labels
onenote.GetPageContent(pageId, out string xml, PageInfo.piAll);
```

That's the core insight this app is built on.
