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

A Didin site signs its own users in: it is its own OpenID Connect issuer at `https://<host>/didin`, and the app is
its public client `didin-app` (registered by the site, limited to that site), using the authorization code flow with
PKCE in the system browser (`Services/SignIn.cs`):

- **Android:** a Custom Tab, back to the app through `se.novaglobenab.didin:/signin` (`SignInCallbackActivity`); the
  refresh token is kept in the platform's secure storage.
- **Windows:** the default browser, back to a one-shot listener on `http://127.0.0.1:<port>/signin`
  (`LoopbackRedirect`); the refresh token is kept encrypted for the Windows user (DPAPI) under the local app data.

The access token stays in memory and goes only into an `Authorization` header. A signed-in person's video plays from a
sealed ticket for that one file, and posters load by their catalogue address, so no token is ever put in a URL. The
last session is picked up when the app starts; signing out revokes both tokens at the site. Without signing in, the
app browses a catalogue that is open to guests. There is no password sign-in.

Not yet: signing in on an Android TV that has no browser (the site's phone sign-in by QR code is the way there).

## Releases

A signed APK is published as a GitHub Release; the signing key comes from the
environment (`DIDIN_KEYSTORE`, `DIDIN_KEYSTORE_PASS`) and never from this repository.
