import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api_client.dart';
import '../../widgets/app_form_field.dart';
import 'auth_controller.dart';

class LoginScreen extends ConsumerStatefulWidget {
  const LoginScreen({super.key});

  static const String path = '/login';

  @override
  ConsumerState<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends ConsumerState<LoginScreen> {
  final _emailController = TextEditingController();
  final _passwordController = TextEditingController();

  Map<String, String> _errors = const {};
  String? _submitError;
  bool _isSubmitting = false;

  @override
  void dispose() {
    _emailController.dispose();
    _passwordController.dispose();
    super.dispose();
  }

  /// Returns a message per invalid field. An empty map means the form is valid.
  Map<String, String> _validate() {
    final errors = <String, String>{};
    final email = _emailController.text.trim();
    final password = _passwordController.text;

    if (email.isEmpty) {
      errors['email'] = 'Email is required.';
    } else if (!RegExp(r'^[^\s@]+@[^\s@]+\.[^\s@]+$').hasMatch(email)) {
      errors['email'] = 'Enter a valid email address.';
    }

    if (password.isEmpty) {
      errors['password'] = 'Password is required.';
    } else if (password.length < 8) {
      // Matches the API's [MinLength(8)].
      errors['password'] = 'Password must be at least 8 characters.';
    }

    return errors;
  }

  Future<void> _submit() async {
    final errors = _validate();
    setState(() {
      _errors = errors;
      _submitError = null;
    });

    if (errors.isNotEmpty) return;

    setState(() => _isSubmitting = true);
    ref.read(authControllerProvider.notifier).clearSessionExpired();

    try {
      await ref
          .read(authControllerProvider.notifier)
          .login(_emailController.text.trim(), _passwordController.text);
      // No navigation here: the router's redirect guard moves us off /login as soon as the
      // auth state flips.
    } on ApiException catch (error) {
      if (mounted) setState(() => _submitError = error.message);
    } finally {
      if (mounted) setState(() => _isSubmitting = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final sessionExpired =
        ref.watch(authControllerProvider.select((state) => state.sessionExpired));

    return Scaffold(
      appBar: AppBar(title: const Text('MaintenX')),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.all(24),
          children: [
            Text('Sign in', style: Theme.of(context).textTheme.headlineSmall),
            const SizedBox(height: 4),
            Text(
              'Use your MaintenX account to continue.',
              style: Theme.of(context).textTheme.bodyMedium,
            ),
            const SizedBox(height: 24),

            if (sessionExpired)
              const _Notice(
                title: 'Session expired',
                message: 'Your access token is no longer valid. Please sign in again.',
              ),
            if (_submitError != null)
              _Notice(title: 'Could not sign in', message: _submitError!),

            AppFormField(
              label: 'Email',
              controller: _emailController,
              errorText: _errors['email'],
              keyboardType: TextInputType.emailAddress,
              enabled: !_isSubmitting,
            ),
            AppFormField(
              label: 'Password',
              controller: _passwordController,
              errorText: _errors['password'],
              obscureText: true,
              enabled: !_isSubmitting,
            ),
            const SizedBox(height: 8),
            FilledButton(
              onPressed: _isSubmitting ? null : _submit,
              child: Text(_isSubmitting ? 'Signing in…' : 'Sign in'),
            ),
          ],
        ),
      ),
    );
  }
}

class _Notice extends StatelessWidget {
  const _Notice({required this.title, required this.message});

  final String title;
  final String message;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Container(
      margin: const EdgeInsets.only(bottom: 16),
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: scheme.errorContainer,
        borderRadius: BorderRadius.circular(8),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            title,
            style: TextStyle(
              fontWeight: FontWeight.bold,
              color: scheme.onErrorContainer,
            ),
          ),
          const SizedBox(height: 2),
          Text(message, style: TextStyle(color: scheme.onErrorContainer)),
        ],
      ),
    );
  }
}
