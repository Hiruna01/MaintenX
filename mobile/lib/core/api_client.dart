import 'dart:async';
import 'dart:convert';

import 'package:http/http.dart' as http;

import 'token_storage.dart';

/// An error carrying the HTTP status, so callers can tell 401 from 403 from 404 — the same
/// distinction the API is careful to make.
class ApiException implements Exception {
  ApiException(this.statusCode, this.message);

  final int statusCode;
  final String message;

  @override
  String toString() => message;
}

const String sessionExpiredMessage =
    'Your session has expired. Please sign in again.';

/// The one place that knows the API's base URL and how a request is authenticated.
///
/// Screens never call `http` directly; they go through a feature API class, which goes
/// through here.
class ApiClient {
  ApiClient({
    required this.baseUrl,
    required TokenStorage tokenStorage,
    http.Client? httpClient,
    this.timeout = const Duration(seconds: 15),
  })  : _tokenStorage = tokenStorage,
        _http = httpClient ?? http.Client();

  final String baseUrl;
  final Duration timeout;

  final TokenStorage _tokenStorage;
  final http.Client _http;

  Uri _uri(String path, [Map<String, String>? query]) {
    final normalised = path.startsWith('/') ? path : '/$path';
    return Uri.parse('$baseUrl$normalised').replace(
      queryParameters: (query == null || query.isEmpty) ? null : query,
    );
  }

  Future<Map<String, String>> _headers({required bool hasBody}) async {
    final headers = <String, String>{'Accept': 'application/json'};
    if (hasBody) {
      headers['Content-Type'] = 'application/json';
    }
    final token = await _tokenStorage.read();
    if (token != null) {
      headers['Authorization'] = 'Bearer $token';
    }
    return headers;
  }

  Future<dynamic> get(String path, {Map<String, String>? query}) async {
    final hadToken = await _tokenStorage.read() != null;
    final response = await _send(
      () async => _http.get(_uri(path, query), headers: await _headers(hasBody: false)),
    );
    return _decode(response, hadToken: hadToken);
  }

  Future<dynamic> post(String path, {Object? body}) async {
    final hadToken = await _tokenStorage.read() != null;
    final response = await _send(
      () async => _http.post(
        _uri(path),
        headers: await _headers(hasBody: body != null),
        body: body == null ? null : jsonEncode(body),
      ),
    );
    return _decode(response, hadToken: hadToken);
  }

  /// Every outbound call carries a timeout, and a socket failure becomes an ApiException
  /// rather than a raw platform exception the UI would have to know about.
  Future<http.Response> _send(Future<http.Response> Function() request) async {
    try {
      return await request().timeout(timeout);
    } on TimeoutException {
      throw ApiException(0, 'The API did not respond in time.');
    } catch (_) {
      throw ApiException(0, 'Could not reach the API.');
    }
  }

  Future<dynamic> _decode(http.Response response, {required bool hadToken}) async {
    if (response.statusCode == 401) {
      // A 401 on a request that carried a token means the token is no longer good: clear
      // secure storage. That notifies the auth controller, which drops the session, and
      // the router's redirect guard sends the user to login. This client knows nothing
      // about screens or navigation.
      if (hadToken) {
        await _tokenStorage.clear();
        throw ApiException(401, sessionExpiredMessage);
      }
      // A 401 on a request that carried no token is an ordinary failed sign-in.
      throw ApiException(401, 'Incorrect email or password.');
    }

    if (response.statusCode == 403) {
      throw ApiException(403, 'You do not have permission to do that.');
    }

    if (response.statusCode >= 400) {
      throw ApiException(response.statusCode, _errorMessage(response));
    }

    if (response.statusCode == 204 || response.body.isEmpty) {
      return null;
    }

    return jsonDecode(response.body);
  }

  /// Reads a ProblemDetails / ValidationProblemDetails body if the API sent one, so the
  /// user sees the API's own message rather than a bare status code.
  String _errorMessage(http.Response response) {
    try {
      final body = jsonDecode(response.body);
      if (body is Map<String, dynamic>) {
        final errors = body['errors'];
        if (errors is Map<String, dynamic> && errors.isNotEmpty) {
          final first = errors.values.first;
          if (first is List && first.isNotEmpty) {
            return first.first.toString();
          }
        }
        if (body['detail'] is String) return body['detail'] as String;
        if (body['title'] is String) return body['title'] as String;
      }
    } catch (_) {
      // Empty or non-JSON body — fall through.
    }
    return 'Request failed with status ${response.statusCode}.';
  }

  void dispose() => _http.close();
}
