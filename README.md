# Nova Globen website

The website of **Nova Globen AB** (nova-globen.se): the company site in English at `/` and Swedish at `/sv/`, the
articles, and the product pages under `/products` (Stomme, Fravashi and Vacha). It is one application on
[Stomme](https://github.com/nova-globen/stomme), Nova Globen's multi-tenant .NET 10 / Blazor application framework, and
ships as one container image, `ghcr.io/nova-globen/nova-globen-website`.

## Layout

| Path | What |
|---|---|
| `NovaGloben.Website/Server` | The host: the site reconciliation that builds both sites from `content/` on every boot, the articles, the contact form, the product addresses and redirects, the security headers |
| `NovaGloben.Website/Client` | The Nova Globen theme and the page, article and contact modules |
| `NovaGloben.Website/Shared`, `Tests` | Types both sides use; the tests |
| `content/sites/company-{en,sv}` | Each site's pages (`site.json` and the page HTML) |
| `content/articles` | The articles (English only), as Markdown with their covers |
| `content/products/<product>/releases.json` | The update manifest a product reads, served at `/products/<product>/releases.json` |
| `scripts/` | The gate (`gate.ps1`): the build, the tests, and the licence, content, contrast and names checks |
| `Dockerfile`, `.github/workflows` | The image, and the workflows that check every change and build the image on a version tag |

## Run it locally

Needs the .NET 10 SDK.

```powershell
dotnet run --project NovaGloben.Website/Server --launch-profile http
```

Browse to `http://localhost:43497`. The first run opens Stomme's installer (choose SQLite); what it writes goes to
`NovaGloben.Website/Server/Data`, which git ignores. Every later boot makes both sites match `content/`.

## Check a change

```powershell
dotnet workload install wasm-tools
pwsh scripts/gate.ps1
pwsh scripts/check-names.ps1
```


## Rights

Copyright Nova Globen AB. All rights reserved. This repository is public for transparency only: no licence is granted
to use, copy, modify or distribute its code, content or brand marks. Third-party components keep their own licences
(for example the Inter font, `OFL.txt`).
