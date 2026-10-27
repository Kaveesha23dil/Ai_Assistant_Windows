# Automations and reminders

Open **Automations**, enter a title, choose **Reminder** or **Summarize a folder**, and enter
the reminder message or the full folder path. Choose the first date, time, and repeat interval,
then select **Create schedule**. For a Friday summary, choose a Friday and **Weekly**.

Schedules retain the time zone of the machine at creation. Daily and weekly schedules keep
the same wall-clock time across daylight-saving changes. A time inside a spring-forward gap
runs at the first valid minute; an ambiguous fall-back time uses standard time and runs once.

The scheduler checks every 10 seconds while the application is running. It does not start
Windows background tasks, wake a sleeping PC, or run after the application closes. When the
app reopens, each overdue schedule runs once, rather than replaying every missed occurrence.
Reminders and completion/failure alerts appear in a banner across the application. The banner's
**View schedules** button opens Automations. These are in-app alerts, not Windows toast notifications.

Select a saved item to see its next run, last run, status, and latest result. **Run now**
executes immediately without changing the saved schedule. **Pause / resume** controls future
scheduled runs; resuming skips occurrences missed while paused. **Delete** removes the item
and its saved result. Running jobs cannot be deleted. Completed one-time schedules stay in
the list for inspection and manual retry; create a new schedule to set another due time.

Folder summaries use the existing document analysis pipeline and configured AI provider.
They require file-search and document-cloud-processing permissions, and cloud AI consent
is enforced by the AI service. They process at most 20 supported files directly inside the
chosen folder, sorted by name. Subfolders and file links/reparse points are excluded. Each
document's summary, references, and extraction warnings appear in the result. Individual
document failures are shown alongside successful summaries; a run with no successful
summaries fails. The source documents are never changed.

Schedules and their latest results are saved in
`%LOCALAPPDATA%\WindowsAIAssistant\automations.json`. This file contains reminder messages,
folder paths, and generated summaries in plain text. Deleting a schedule removes its saved
content. Writes replace the file atomically; invalid JSON is reported rather than silently
overwritten. The scheduler is designed for one running application instance.

An occurrence is recorded before it starts, preventing accidental replay after a crash. A run
interrupted by shutdown or a crash is marked interrupted; use **Run now** to retry it. Failed
recurring runs keep their next scheduled occurrence and do not retry continuously.

## Verification

```powershell
dotnet build WindowsAIAssistant.sln -p:Platform=x64 -p:UseSharedCompilation=false -m:1
dotnet test tests/WindowsAIAssistant.Application.Tests/WindowsAIAssistant.Application.Tests.csproj -p:Platform=x64 -p:UseSharedCompilation=false -m:1 --filter FullyQualifiedName~AutomationTests
```
