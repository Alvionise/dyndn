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

- Follow the existing code style and project conventions; `.editorconfig` is the source of truth and the
  CI runs `dotnet format --verify-no-changes`. Apply the fixes locally with `dotnet format .\dyndn.slnx`.
- The style the project settled on: file-scoped namespaces with `System.*` usings first, collection
  expressions (`[]`, `["a", "b"]`) instead of `new[] { }` / `new List<T> { }`, explicit constructors
  instead of primary constructors, and `var` only when the type is obvious from the right-hand side.
- Keep changes focused and avoid unrelated refactors in the same pull request.
- Add or update tests for any behavior change in the pure logic (normalization, RCI payloads,
  password protection).
- Do not commit local data (`src/DyndDns.TrayApp/config/dyndns.db` and the logs) or anything under
  `bin/`, `obj/`, `dist/`.
- Update `CHANGELOG.md` under `Unreleased` for user-visible changes.

## Pull requests

- Describe what changed and why.
- Reference the related issue when one exists.
- Make sure the build and tests pass before requesting a review.
