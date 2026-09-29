import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/api_client.dart';
import '../../core/app_theme.dart';
import '../../widgets/app_form_field.dart';
import '../../widgets/surfaces.dart';
import 'auth_controller.dart';
import 'login_screen.dart';

/// Returns a message per invalid field; an empty map means the form is valid. The limits are
/// the API's RegisterRequest: email at most 256, password 8 to 128, full name at most 200. The
/// confirmation is the phone's own check — the API never sees it.
Map<String, String> validateRegistration({
  required String fullName,
  required String email,
  required String password,
  required String confirmPassword,
}) {
  final errors = <String, String>{};
  final name = fullName.trim();
  final address = email.trim();

  if (name.isEmpty) {
    errors['fullName'] = 'Your name is required.';
  } else if (name.length > 200) {
    errors['fullName'] = 'Your name must be at most 200 characters.';
  }

  if (address.isEmpty) {
    errors['email'] = 'Email is required.';
  } else if (!RegExp(r'^[^\s@]+@[^\s@]+\.[^\s@]+$').hasMatch(address)) {
    errors['email'] = 'Enter a valid email address.';
  } else if (address.length > 256) {
    errors['email'] = 'Email must be at most 256 characters.';
  }

  if (password.isEmpty) {
    errors['password'] = 'Password is required.';
  } else if (password.length < 8) {
    errors['password'] = 'Password must be at least 8 characters.';
  } else if (password.length > 128) {
    errors['password'] = 'Password must be at most 128 characters.';
  }

  if (confirmPassword != password) {
    errors['confirmPassword'] = 'The passwords do not match.';
  }

  return errors;
}

/// Self-registration — for REPORTERS, the people who file faults from the phone.
///
/// The request carries no role, and the API makes every anonymous registration a Reporter;
/// technician, manager and admin accounts are created by an Admin. So this screen cannot hand
/// anyone more than a reporter's access, whatever is typed into it.
///
/// On success the API's 201 already carries a token: the account is signed in and the router's
/// redirect guard moves off this screen, exactly as it does after a login.
class RegisterScreen extends ConsumerStatefulWidget {
  const RegisterScreen({super.key});

  static const String path = '/register';

  @override
  ConsumerState<RegisterScreen> createState() => _RegisterScreenState();
}

class _RegisterScreenState extends ConsumerState<RegisterScreen> {
  final _nameController = TextEditingController();
  final _emailController = TextEditingController();
  final _passwordController = TextEditingController();
  final _confirmController = TextEditingController();

  Map<String, String> _errors = const {};
  String? _submitError;
  bool _isSubmitting = false;

  /// Display only: whether the passwords are shown as typed.
  bool _showPassword = false;

  @override
  void dispose() {
    _nameController.dispose();
    _emailController.dispose();
    _passwordController.dispose();
    _confirmController.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    final errors = validateRegistration(
      fullName: _nameController.text,
      email: _emailController.text,
      password: _passwordController.text,
      confirmPassword: _confirmController.text,
    );
    setState(() {
      _errors = errors;
      _submitError = null;
    });

    if (errors.isNotEmpty) return;

    setState(() => _isSubmitting = true);

    try {
      await ref.read(authControllerProvider.notifier).register(
            fullName: _nameController.text.trim(),
            email: _emailController.text.trim(),
            password: _passwordController.text,
          );
      // No navigation here: the router's redirect guard moves us off this screen as soon as
      // the auth state flips, the same as after a login.
    } on ApiException catch (error) {
      if (mounted) setState(() => _submitError = error.message);
    } finally {
      if (mounted) setState(() => _isSubmitting = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    Widget visibilityToggle() => Padding(
          padding: const EdgeInsets.only(right: 6),
          child: IconButton(
            tooltip: _showPassword ? 'Hide passwords' : 'Show passwords',
            icon: Icon(_showPassword ? LucideIcons.eyeOff : LucideIcons.eye, size: 20),
            onPressed: () => setState(() => _showPassword = !_showPassword),
          ),
        );

    return Scaffold(
      backgroundColor: MxColors.surface,
      body: SafeArea(
        child: Center(
          child: ConstrainedBox(
            // A phone fills it; a tablet gets a column, not a stretched form.
            constraints: const BoxConstraints(maxWidth: 460),
            child: ListView(
              padding: const EdgeInsets.fromLTRB(24, 16, 24, 32),
              children: [
                Align(
                  alignment: Alignment.centerLeft,
                  child: MxRoundButton(
                    icon: LucideIcons.arrowLeft,
                    tooltip: 'Back to sign in',
                    onPressed: _isSubmitting ? null : () => context.go(LoginScreen.path),
                  ),
                ),
                const SizedBox(height: 22),
                Text('Create an account', style: theme.textTheme.displaySmall),
                const SizedBox(height: 8),
                Text(
                  'For reporting faults on campus. Staff accounts are issued by your facilities team.',
                  style: theme.textTheme.bodyLarge?.copyWith(color: MxColors.graphite),
                ),
                const SizedBox(height: 26),
                if (_submitError != null)
                  Container(
                    margin: const EdgeInsets.only(bottom: 16),
                    padding: const EdgeInsets.all(14),
                    decoration: BoxDecoration(
                      color: MxColors.redBg,
                      border: Border.all(color: MxColors.redLine),
                      borderRadius: BorderRadius.circular(MxRadii.md),
                    ),
                    child: Row(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        const Icon(LucideIcons.circleAlert, size: 18, color: MxColors.red),
                        const SizedBox(width: 10),
                        Expanded(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Text(
                                'Could not create the account',
                                style: theme.textTheme.titleSmall?.copyWith(color: MxColors.red),
                              ),
                              const SizedBox(height: 2),
                              Text(
                                _submitError!,
                                style: theme.textTheme.bodyMedium?.copyWith(color: MxColors.red),
                              ),
                            ],
                          ),
                        ),
                      ],
                    ),
                  ),
                AutofillGroup(
                  child: Column(
                    children: [
                      AppFormField(
                        label: 'Full name',
                        controller: _nameController,
                        errorText: _errors['fullName'],
                        textInputAction: TextInputAction.next,
                        autofillHints: const [AutofillHints.name],
                        enabled: !_isSubmitting,
                      ),
                      AppFormField(
                        label: 'Email',
                        controller: _emailController,
                        errorText: _errors['email'],
                        keyboardType: TextInputType.emailAddress,
                        textInputAction: TextInputAction.next,
                        autofillHints: const [AutofillHints.email],
                        enabled: !_isSubmitting,
                      ),
                      AppFormField(
                        label: 'Password',
                        controller: _passwordController,
                        errorText: _errors['password'],
                        hintText: 'At least 8 characters',
                        obscureText: !_showPassword,
                        textInputAction: TextInputAction.next,
                        autofillHints: const [AutofillHints.newPassword],
                        enabled: !_isSubmitting,
                        suffixIcon: visibilityToggle(),
                      ),
                      AppFormField(
                        label: 'Confirm password',
                        controller: _confirmController,
                        errorText: _errors['confirmPassword'],
                        obscureText: !_showPassword,
                        textInputAction: TextInputAction.done,
                        onSubmitted: (_) => _isSubmitting ? null : _submit(),
                        autofillHints: const [AutofillHints.newPassword],
                        enabled: !_isSubmitting,
                      ),
                    ],
                  ),
                ),
                const SizedBox(height: 10),
                FilledButton(
                  onPressed: _isSubmitting ? null : _submit,
                  style: FilledButton.styleFrom(
                    disabledBackgroundColor: MxColors.ink2,
                    disabledForegroundColor: Colors.white,
                  ),
                  child: _isSubmitting
                      ? const Row(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            SizedBox(
                              width: 16,
                              height: 16,
                              child: CircularProgressIndicator(
                                strokeWidth: 2,
                                color: Colors.white,
                                strokeCap: StrokeCap.round,
                              ),
                            ),
                            SizedBox(width: 12),
                            Text('Creating account…'),
                          ],
                        )
                      : const Text('Create account'),
                ),
                const SizedBox(height: 14),
                TextButton(
                  onPressed: _isSubmitting ? null : () => context.go(LoginScreen.path),
                  child: const Text('Already have an account? Sign in'),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
