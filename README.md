# Windows AI Assistant

An AI-powered Windows 11 desktop assistant built with C# / .NET 10 and WinUI 3.

> **Status:** early development. This repository currently contains the initial project bootstrap only.

## Project Overview

Windows AI Assistant is a native Windows desktop application designed to become an AI-powered
system assistant with support for chat, voice commands, Windows system controls, application
launching, local file search, document Q&A, clipboard AI, screenshots, notifications, tasks,
reminders, workflow automation, local AI, and cloud AI APIs.

The architecture is built around clean, layered, provider-independent design so that the UI,
application logic, and infrastructure integrations remain separated and testable.

## Technology Stack

| Area            | Choice                                                          |
|-----------------|----------------------------------------------------------------|
| Language        | C# (latest, nullable reference types enabled)                   |
| Runtime         | .NET 10 (LTS)                                                   |
| UI              | WinUI 3 / XAML (Windows App SDK 2.5.x)                          |
| Target          | Windows 11, x64 (WindApp SDK unpackaged, self-contained)        |
| MVVM            | CommunityToolkit.Mvvm                                           |
| DI / Hosting    | Microsoft.Extensions.Hosting / DependencyInjection             |
| Logging         | Microsoft.Extensions.Logging                                    |
| Configuration   | Microsoft.Extensions.Configuration                             |
| Data            | SQLite-ready architecture (database not implemented yet)        |
| Testing         | xUnit + Microsoft.NET.Test.Sdk                                  |

## Prerequisites

- Windows 11 (x64)
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (or later)
- Visual Studio 2022 17.14+ with the **Windows application development** workload
  (optional — the solution can be built from the CLI with `dotnet`)
- Developer Mode enabled in Windows Settings > For developers (Windows App SDK tooling)

## Installation

```bash
git clone https://github.com/Kaveesha23dil/Ai_Assistant_Windows.git
cd Ai_Assistant_Windows
```

## Build

```bash
dotnet restore
dotnet build WindowsAIAssistant.sln -p:Platform=x64
```

## Run

```bash
dotnet run --project src/WindowsAIAssistant.App -p:Platform=x64
```

## Test

```bash
dotnet test
```

## Folder Structure

```text
├── src/
│   ├── WindowsAIAssistant.App/            # WinUI 3 shell, views, view models, navigation
│   ├── WindowsAIAssistant.Core/           # Domain models, interfaces, enums, shared primitives
│   ├── WindowsAIAssistant.Application/    # Use cases, services, commands, queries, DTOs
│   └── WindowsAIAssistant.Infrastructure/ # AI providers, Windows APIs, storage, integrations
├── tests/
│   ├── WindowsAIAssistant.Core.Tests/
│   └── WindowsAIAssistant.Application.Tests/
├── docs/                                  # Architecture & development documentation
├── scripts/                               # Build / developer scripts
├── .editorconfig
├── .gitignore
├── Directory.Build.props                  # Shared build properties
├── Directory.Packages.props               # Centralized NuGet package versions (CPM)
└── WindowsAIAssistant.sln
```

## Git Workflow

This project uses a strict branch-based workflow — see [docs/git-workflow.md](docs/git-workflow.md).

```text
main
└── develop
    ├── chore/project-bootstrap
    ├── feat/core-architecture
    ├── ...
    └── test/core-tests
```

Conventional Commits are used for all commit messages. No feature work is ever committed
directly to `main` or `develop`.

## Architecture Summary

Layered clean architecture where dependency direction always points downward:

```text
        App (WinUI 3)          Infrastructure (AI, Windows APIs, storage)
              │                              │
              ▼                              │
        Application <────── implements Core interfaces
              │
              ▼
            Core
```

- `Core` references nothing and contains no UI or platform code.
- `Application` holds use cases and orchestration and is UI-agnostic.
- `Infrastructure` implements `Core` interfaces and is registered through dependency injection.
- `App` communicates with the rest of the system only through ViewModels and services.

## Future Roadmap

Local file search now uses the real filesystem with location/type/date/size filters, local
text previews, and Windows file/folder actions. See [Local file search](docs/file-search.md).

Automations now support persistent one-time, daily, and weekly reminders and folder summaries.
They run while the app is open, with in-app alerts and one catch-up run after reopening.
See [Automations and reminders](docs/automations.md) for setup, permissions, and limitations.

See [docs/roadmap.md](docs/roadmap.md) for the planned feature roadmap. In short:

AI chat, voice commands, Windows system controls, application launching, local file search,
chat with documents, clipboard AI, screenshot/screen understanding, notifications, tasks and
reminders, AI workflow automation, local AI support, cloud AI APIs, system monitoring, and a
plugin/skills system.

> **Security note:** no credentials or API keys are ever stored in the repository.
