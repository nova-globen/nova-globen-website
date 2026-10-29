# NovaGloben.Website

The website as a Stomme application: `Server` (the host), `Client` (the theme and modules, also the WebAssembly
client), `Shared` and `Tests`. The repository's [README](../README.md) says what it serves and how to run it.

- `Directory.Packages.props` pins the Stomme packages and everything else this app builds against.
- `Server/Program.cs` runs Stomme's own host. The website's own services and middleware are in
  `Server/Startup/ServerStartup.cs`, which the host discovers by convention.
- `Server/appsettings.json` holds the defaults and is committed. What an installation writes goes to
  `appsettings.override.json` in the directory `STOMME_APPSETTINGS_PATH` names (`Server/Data` when unset); a container
  points it at a mounted directory.
- The sites are not edited in the admin pages: `Server/Sites/SiteReconciler.cs` makes them match `content/` on every
  boot.
