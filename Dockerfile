# The nova-globen-website image: the Stomme host with the company website (English at /, Swedish at /sv/) and the
# product pages. One image serves both hosts; which host it answers as is set where it is deployed (Installation
# settings and the data folder are mounted there), never here.
#
# The solution has a Blazor WebAssembly client project, so publishing the server needs the wasm-tools workload
# (NETSDK1112 without it). The page and article text is embedded; the articles' cover images are copied to the publish
# output. BLAZOR106: `dotnet publish` is incremental and never cleans - do not add `dotnet clean` or delete obj/.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
RUN dotnet workload install wasm-tools
WORKDIR /src
COPY . .
RUN dotnet publish NovaGloben.Website/Server/NovaGloben.Website.Server.csproj \
      -c Release --self-contained false -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# curl is only for the HEALTHCHECK.
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /out ./
# Non-root. The writable paths that are mounted at runtime are created here so they belong to the app's user:
#   /app/Data             the SQLite files, appsettings.override.json (STOMME_APPSETTINGS_PATH) and the local backups
#   /app/Content          private tenant files and the error log
#   /app/wwwroot/Content  public tenant files
#   /home/app/.aspnet/DataProtection-Keys  the key ring, so the contact form's token and sign-ins survive a restart
# The base image already has a user named app (uid 1654); the site runs as uid/gid 10001, which the host's folders
# belong to, with /home/app as its home.
RUN groupadd -g 10001 website \
 && useradd -u 10001 -g 10001 -M -d /home/app -s /usr/sbin/nologin website \
 && mkdir -p /app/Data /app/Content /app/wwwroot/Content /home/app/.aspnet/DataProtection-Keys \
 && chown -R 10001:10001 /app /home/app
USER 10001:10001
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://0.0.0.0:8080 \
    HOME=/home/app \
    STOMME_APPSETTINGS_PATH=/app/Data \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080
# /healthz answers for any host, so the check names none.
HEALTHCHECK --interval=15s --timeout=5s --start-period=120s --retries=6 \
  CMD curl -fsS http://127.0.0.1:8080/healthz -o /dev/null || exit 1
ENTRYPOINT ["dotnet", "NovaGloben.Website.Server.Stomme.dll"]
