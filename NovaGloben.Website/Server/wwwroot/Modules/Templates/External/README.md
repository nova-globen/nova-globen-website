# [Owner].Module.[Module]

[Description]

A Stomme module in the framework's reference shape - the KnowledgeBase exemplar's, reduced to one aggregate
(`examples/Modules/KnowledgeBase` in the Stomme repository shows every extension point):

| Project | Holds | Runs |
| --- | --- | --- |
| `Shared` | the wire models, the permission keys, the service contract | server and browser |
| `Client` | the page (`ModuleBase`), `ModuleInfo` (definition and keys), the HTTP service | server and browser |
| `Server` | the domain, the storage-blind persistence, the commands and queries, the API, the server manager | server only |
| `Package` | the nuspec that packs the three assemblies | build machine |

- **Independent:** each project references Stomme through ONE package, `Stomme.Sdk.Slim`, never the framework's source.
- **Storage-blind and tenant-owned:** `[Module]Entry` is persisted by the host on whichever store the installation chose;
  it carries its tenant and its site. The module writes no `DbContext`, store or migration.
- **Gated:** the keys `[ModuleKey]:view` and `[ModuleKey]:edit`, declared in `ModuleInfo` with the roles a new site grants;
  every request carries `[Requires]`, every API action `[RequiresPermissionKey]`.

Build and pack, then drop the package into a host's `Packages` folder (or install it from the host's module management):

```powershell
pwsh pack.ps1 -PackagesFolder <host content root>/Packages
```
