# The Didin app

Didin for Android phones, Android TV and Windows: the Didin module's own UI (`NovaGloben.Website/Modules/Didin/Ui`) in
a .NET MAUI window, talking to a Didin site's API.

## Build

It needs the .NET 10 SDK and the `maui-android` and `maui-windows` workloads (`dotnet workload install maui-android
maui-windows`). Build one target at a time:

```
dotnet build apps/didin/Didin.App/Didin.App.csproj -f net10.0-windows10.0.19041.0
dotnet build apps/didin/Didin.App/Didin.App.csproj -f net10.0-android
```

The site it talks to is set at build time: `-p:DidinSite=https://<host>/didin`. A release build defaults to
`https://nova-globen.se/didin`, a debug build to `http://localhost:5100/didin`. On Windows, `DIDIN_SERVER` overrides it
when the app starts.

## Signing in

A Didin site signs its own users in: it is its own OpenID Connect issuer at `https://<host>/didin/`, and the app is a
public client of that site using the authorization code flow with PKCE. That arrives with the next version of the
site's platform. Until then the app has no sign-in at all (`Services/SignIn.cs`): it browses a catalogue that is open
to guests, and every account's own feature is hidden, as for a guest on the site. There is no password sign-in.

## Releases

None until signing in works. Then a signed APK is published as a GitHub Release; the signing key comes from the
environment (`DIDIN_KEYSTORE`, `DIDIN_KEYSTORE_PASS`) and never from this repository.
