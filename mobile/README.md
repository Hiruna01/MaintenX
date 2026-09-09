# mobile — MaintenX Flutter client

Flutter + Riverpod + go_router. Talks only to the ASP.NET Core API in `api/` — never to
the Python agent service.

## First run

The platform folders (`android/`, `ios/`, …) are not in the repo. Generate them once, in
this directory — `flutter create` skips files that already exist, so `lib/` and
`pubspec.yaml` are left alone. **Do not pass `--overwrite`.**

```bash
cd mobile
flutter create . --project-name maintenx_mobile --platforms=android,ios
flutter pub get
```

Then run against a local API:

```bash
# Android emulator — 10.0.2.2 is the emulator's alias for the host's localhost
flutter run --dart-define=API_BASE_URL=http://10.0.2.2:5138

# iOS simulator — shares the host's network
flutter run --dart-define=API_BASE_URL=http://localhost:5138
```

`API_BASE_URL` is a compile-time define read by `String.fromEnvironment` in
`lib/core/env.dart`, so it is baked into the binary — there is no `.env` shipped in the
app bundle. Nothing secret goes in it: a `--dart-define` is readable from the compiled
app, exactly like a `VITE_` variable is readable in the web bundle.

Android needs `minSdkVersion 18` or higher for flutter_secure_storage; the default from
`flutter create` is already above that.

## Structure

```
lib/core/         env, api_client, token_storage, infrastructure providers
lib/router/       go_router with the redirect guard
lib/widgets/      LoadingView, EmptyView, ErrorView, AppFormField, AppDropdownField
lib/features/<name>/   the screens, their API class and their providers
```

Screens never call `http` directly. A screen calls a feature API class (`AuthApi`,
`ReportsApi`), which calls the single `ApiClient`.

## The token is in flutter_secure_storage, not SharedPreferences

SharedPreferences is an unencrypted XML file on Android and a plist on iOS, inside the app
sandbox: readable on a rooted or jailbroken device and extractable from an `adb backup`.
`TokenStorage` keeps the access token in the iOS Keychain and Android's
EncryptedSharedPreferences instead. That is the entire reason the dependency is there, and
it is the answer if anyone asks where the JWT lives.

The API issues one access token with a 12-hour lifetime and no refresh token, so there is
exactly one value to store and nothing to rotate.

## How a 401 signs you out

`ApiClient` knows nothing about screens. When a request that carried a token comes back
401, it clears `TokenStorage`. `TokenStorage` is a `ChangeNotifier`, so `AuthController`
hears the token disappear and moves the session to signed-out. go_router's
`refreshListenable` sees the status change and its `redirect` sends the user to `/login`,
where a "session expired" notice is shown.

A 401 on a request that carried **no** token — signing in with the wrong password — is not
a session expiry and is reported as a failed sign-in instead.

## Routing

`redirect` in `lib/router/app_router.dart` is the only guard: an unauthenticated user can
reach `/login` and nothing else, and an authenticated user is bounced off `/login`. There
is no per-screen check to forget. While secure storage is being read the status is
`unknown` and `main.dart` shows a spinner, so a returning user is never flashed the login
screen before their stored token has been checked.

## There is no chat interface

Not here and not later. When the agent needs more detail, its clarification questions will
be rendered as a **form with bounded inputs** — pickers, yes/no, short text — never as a
message thread. `SubmitReportScreen` carries the same note.

## Known gap: POST /api/reports does not exist yet

`ReportsApi.submit()` posts `{ description, roomId }` to `/api/reports`. There is no
`ReportsController` in `api/` yet, and `StartWorkflowRequest.ReportId` is commented
"Optional for now; reports are a later feature". **Submitting a report returns 404 until
that endpoint lands.** Everything else on the screen — the room picker, validation, the
three request states — works against the real API today, and only that one method changes
when the endpoint arrives.

The room picker reads `GET /api/rooms`, which does exist.

## Hooks left for later

Photo attachment and QR scanning are disabled buttons on the submit form, marked with
`TODO(photo)` and `TODO(qr)`. They are visible rather than hidden so the finished shape of
the form is obvious, and wiring them up is a change in one place.
