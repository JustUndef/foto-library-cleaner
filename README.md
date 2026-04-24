# Foto Library Cleaner

Windows-first duplicate photo review app built with WPF and .NET 10.

## Current status

The repository contains the initial application shell:

- WPF desktop app (`FotoLibraryCleaner.App`)
- Core project for duplicate-scan logic (`FotoLibraryCleaner.Core`)
- Mock scan service to drive the first review UI
- Folder selection, scan options, duplicate group list, and side-by-side review layout

## Planned next steps

1. Replace the mock service with the real duplicate detection engine.
2. Add image preview loading from disk.
3. Persist scan sessions and review decisions with SQLite.
4. Add execution flow for move, keep, and skip actions.

## Build

This solution targets `.NET 10`.

If the .NET 10 SDK is not yet installed on the machine, install it first and then run:

```powershell
.\scripts\build.ps1
.\scripts\run-app.ps1
```
