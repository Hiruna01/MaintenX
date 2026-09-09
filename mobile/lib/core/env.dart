/// Build-time configuration.
///
/// Read with `String.fromEnvironment`, which is resolved by the compiler, so the value is
/// baked into the binary and there is no `.env` file shipped inside the app bundle for
/// anyone to read. Pass it on the command line:
///
///   flutter run --dart-define=API_BASE_URL=http://10.0.2.2:5138
///
/// Nothing secret belongs here: a `--dart-define` is visible in the compiled app, exactly
/// like `VITE_` variables are visible in the web bundle.
class Env {
  const Env._();

  /// Base URL of the ASP.NET Core API. The mobile client talks to this API and nothing
  /// else — never to the Python agent service.
  ///
  /// The default is the Android emulator's alias for the host machine's localhost. An iOS
  /// simulator shares the host's network, so it needs
  /// `--dart-define=API_BASE_URL=http://localhost:5138`.
  static const String apiBaseUrl = String.fromEnvironment(
    'API_BASE_URL',
    defaultValue: 'http://10.0.2.2:5138',
  );
}
