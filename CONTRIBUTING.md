# Contributing

Thanks for taking the time to contribute.

## Requirements

- Windows 10/11 x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download)

## Build

```powershell
dotnet build .\dyndn.slnx -c Release
```

## Test

```powershell
dotnet test .\tests\DyndDns.TrayApp.Tests\DyndDns.TrayApp.Tests.csproj -c Release
```

## Run

```powershell
dotnet run --project .\src\DyndDns.TrayApp\DyndDns.TrayApp.csproj
```

## Publish

```powershell
.\publish.ps1
```

## Guidelines

- Follow the existing code style and project conventions; `.editorconfig` defines the defaults.
- Keep changes focused and avoid unrelated refactors in the same pull request.
- Add or update tests for any behavior change in the pure logic (normalization, RCI payloads,
  password protection).
- Do not commit local configuration (`src/DyndDns.TrayApp/config/dyndns.json`,
  `src/DyndDns.TrayApp/config/dns-list.json`) or anything under `bin/`, `obj/`, `dist/`.
- Update `CHANGELOG.md` under `Unreleased` for user-visible changes.

## Pull requests

- Describe what changed and why.
- Reference the related issue when one exists.
- Make sure the build and tests pass before requesting a review.
