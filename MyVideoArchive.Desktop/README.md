# MyVideoArchive.Desktop

Electron-wrapped desktop build of MyVideoArchive, powered by
[ElectronNET.Core](https://github.com/ElectronNET/Electron.NET).

The original `MyVideoArchive` web project is **not** modified — it still runs as a
normal ASP.NET Core MVC app and ships via Docker. This project is purely additive:
it produces a second binary (`MyVideoArchive.Desktop`) that hosts the same web app
inside an Electron window.

## How code is shared

Rather than maintaining two parallel codebases, this project links the source from
`..\MyVideoArchive\` into its own assembly via `Compile` / `Content` items in the
`.csproj`:

- All C# files (controllers, infrastructure, extensions, view models, global
  usings, area pages, …) are linked in via a `Compile Include` glob.
- Razor views under `Views/` and `Areas/` are linked in as `Content`, so the Razor
  SDK compiles them into this project's assembly.
- `wwwroot/` is exposed at runtime by pointing `WebRootPath` at the sibling
  `..\MyVideoArchive\wwwroot` during dev runs, and copied into `publish\wwwroot`
  by MSBuild for packaging.
- Library projects (`MyVideoArchive.Data`, `MyVideoArchive.Services`,
  `MyVideoArchive.Models`) are referenced normally.

The only file that is _not_ shared is `Program.cs` — this project has its own
Electron-aware entry point that calls `builder.UseElectron(args, …)` and creates
the main browser window.

## Prerequisites

- .NET 10 SDK (same as the web project)
- Node.js 22.x or later (required by ElectronNET.Core to spawn the Electron host)
- A reachable PostgreSQL database, configured via either:
  - User Secrets:
    `dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<conn-string>"`
  - The `DefaultConnection` value in `appsettings.json` next to the executable
  - The `ConnectionStrings__DefaultConnection` environment variable

## Running

### As a regular ASP.NET Core app (web)

```
dotnet run --project MyVideoArchive.Desktop
```

`builder.UseElectron(...)` is a no-op when the process isn't launched by the
Electron host, so the desktop project can also serve as a vanilla web app for
quick iteration.

### As an Electron desktop app

ElectronNET.Core launches the Electron host automatically when the project is
started under its tooling. The simplest end-to-end flow:

```
dotnet build MyVideoArchive.Desktop
dotnet run  --project MyVideoArchive.Desktop
```

When packaging for distribution, `dotnet publish` will pick up the linked
`Content` items so the published output contains a real `wwwroot` and `Views`
tree alongside the executable. See the
[ElectronNET.Core wiki](https://github.com/ElectronNET/Electron.NET/wiki) for
packaging details and the auto-generated `Properties\electron-builder.json`
that gets created on first build.

## What this project does **not** do

- It does **not** replace or modify the existing `MyVideoArchive` web project or
  its Docker setup. Both can be built and run independently.
- It does **not** change the database schema. Both versions point at the same
  PostgreSQL database (whichever one your connection string targets).
