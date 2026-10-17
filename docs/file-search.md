# Local file search

Open **Files**, enter a name or a short search phrase, and select **Search Files**.
Examples:

- `invoice` — names containing invoice, ignoring case.
- `Find the invoice PDF I downloaded last week` — PDF names containing invoice in Downloads,
  created during the previous Monday through Sunday.
- `*.pdf` — PDFs, or `*` for every file matching the controls.
- `"my last week.pdf"` — a literal name; the words inside these quotes are not interpreted.

The deterministic parser recognizes common document/image extensions, Downloads, Documents,
Desktop, today, yesterday, last 7 days, last week, and this month. It uses no AI or network
service. Remaining words must all occur in the file name; this is not document-content or
semantic search. Last week is a calendar week in the machine's local time zone, with an
inclusive Monday start and exclusive following-Monday end. Downloaded and created use file
creation time. Creation time is a filesystem timestamp, not a verified browser download date,
and may change when files are copied or restored.

## Filters and scope

Use **Location** to select Downloads, Documents, Desktop, Pictures, Music, Videos, or a custom
folder. The default searches available common user folders, unless the query names a location.
Custom folders require a full existing path. Windows supplies known-folder paths, including
redirected user folders. There is no disk-wide crawl or background index.

Optional extension, date, timestamp, and size controls override the corresponding query hints.
The size ranges use binary megabytes (1 MB = 1,048,576 bytes). Turn off **Include subfolders**
to search only the selected folder. Searches check file-search permission throughout execution
and can be cancelled. Navigating away also cancels the active search and previews.

The filesystem scan runs off the UI thread and is bounded to 50,000 entries or approximately
15 seconds (a blocking filesystem call can take longer). It returns the best 200 matches,
ordered by name relevance, then most recently modified, then path. Results report scanned
roots, applied date/type filters, skipped entries, and partial results when a limit is reached.
Unavailable roots are omitted; inaccessible directories are skipped. System and reparse-point
entries, including cloud placeholders, are skipped. Dot-prefixed subdirectories are not
traversed. Narrow the location or filters if the results are incomplete.

## Previews and actions

Each result shows its full path, type, size, and modification date. **Preview** extracts text
locally using the existing document readers (PDF, DOCX, XLSX, PPTX, and supported text formats).
It sends nothing to an AI provider and requires only file-search permission. Previews accept
files up to 8 MB, show up to 12,000 extracted characters, and display extraction warnings.
Images and other unsupported types show an explanatory message. Scanned PDFs may have no
extractable text. Close the preview to clear the displayed content.

**Open** launches the file in its default Windows application. **Open Folder** opens its
containing folder. Both require file-search and application-launch permissions; missing files
and launch failures produce a visible message. **Summarize** and **Ask AI** open the Documents
page, where the existing document/cloud permissions apply. Unsupported document formats have
those AI actions disabled. Opening a file or folder never constructs a shell command line.

The production service replaces the former mock for the Files page, voice commands, and agent
file-search tools. Explicit development-service registration continues to provide deterministic
mock results for demonstrations and tests.

## Validation

```powershell
dotnet build WindowsAIAssistant.sln -p:Platform=x64 -p:UseSharedCompilation=false -m:1
dotnet test tests/WindowsAIAssistant.Application.Tests/WindowsAIAssistant.Application.Tests.csproj -p:Platform=x64 -p:UseSharedCompilation=false -m:1
```
