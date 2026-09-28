import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';
import 'package:qr_flutter/qr_flutter.dart';

import '../../core/api_client.dart';
import '../../core/app_theme.dart';
import '../../widgets/app_form_field.dart';
import '../../widgets/surfaces.dart';
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

  /// Display only: whether the password is shown as typed.
  bool _showPassword = false;

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
    final theme = Theme.of(context);
    final sessionExpired =
        ref.watch(authControllerProvider.select((state) => state.sessionExpired));

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
                const Align(alignment: Alignment.centerLeft, child: MxWordmark()),
                const SizedBox(height: 22),
                const _StickerHero(),
                const SizedBox(height: 30),
                Text('Sign in', style: theme.textTheme.displaySmall),
                const SizedBox(height: 8),
                Text(
                  'Use the account your facilities team issued you.',
                  style: theme.textTheme.bodyLarge?.copyWith(color: MxColors.graphite),
                ),
                const SizedBox(height: 26),

                // Two different answers, two different colours: an expired session is not
                // the user's mistake, a refused sign-in is.
                if (sessionExpired)
                  const _Notice(
                    title: 'Session expired',
                    message: 'Your access token is no longer valid. Please sign in again.',
                    background: MxColors.amberBg,
                    border: MxColors.amberLine,
                    foreground: MxColors.amber,
                    icon: LucideIcons.clock,
                  ),
                if (_submitError != null)
                  _Notice(
                    title: 'Could not sign in',
                    message: _submitError!,
                    background: MxColors.redBg,
                    border: MxColors.redLine,
                    foreground: MxColors.red,
                    icon: LucideIcons.circleAlert,
                  ),

                AutofillGroup(
                  child: Column(
                    children: [
                      AppFormField(
                        label: 'Email',
                        controller: _emailController,
                        errorText: _errors['email'],
                        keyboardType: TextInputType.emailAddress,
                        textInputAction: TextInputAction.next,
                        autofillHints: const [AutofillHints.email, AutofillHints.username],
                        enabled: !_isSubmitting,
                      ),
                      AppFormField(
                        label: 'Password',
                        controller: _passwordController,
                        errorText: _errors['password'],
                        obscureText: !_showPassword,
                        textInputAction: TextInputAction.done,
                        onSubmitted: (_) => _isSubmitting ? null : _submit(),
                        autofillHints: const [AutofillHints.password],
                        enabled: !_isSubmitting,
                        suffixIcon: Padding(
                          padding: const EdgeInsets.only(right: 6),
                          child: IconButton(
                            tooltip: _showPassword ? 'Hide password' : 'Show password',
                            icon: Icon(
                              _showPassword ? LucideIcons.eyeOff : LucideIcons.eye,
                              size: 20,
                            ),
                            onPressed: () => setState(() => _showPassword = !_showPassword),
                          ),
                        ),
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
                            Text('Signing in…'),
                          ],
                        )
                      : const Text('Sign in'),
                ),
                const SizedBox(height: 22),
                Text(
                  'Forgotten your password? Your facilities team can reset it.',
                  textAlign: TextAlign.center,
                  style: theme.textTheme.bodySmall,
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

/// The one illustration in the app: an asset sticker as it is printed and stuck on a
/// machine — a real QR code of a seeded tag, which the scanner would resolve — on a dotted
/// panel, with the outcome the whole system exists for pinned to its corner.
///
/// It settles into its tilt once when the screen opens, and holds still when the phone
/// asks for reduced motion.
class _StickerHero extends StatelessWidget {
  const _StickerHero();

  static const String _tag = 'PRJ-MAB101-01';

  @override
  Widget build(BuildContext context) {
    final reduceMotion = MediaQuery.maybeDisableAnimationsOf(context) ?? false;

    return ExcludeSemantics(
      child: SizedBox(
        height: 232,
        child: Stack(
          clipBehavior: Clip.none,
          children: [
            Positioned.fill(
              child: ClipRRect(
                borderRadius: BorderRadius.circular(MxRadii.xl),
                child: const ColoredBox(
                  color: MxColors.well,
                  child: CustomPaint(painter: _DotGridPainter()),
                ),
              ),
            ),
            Center(
              child: TweenAnimationBuilder<double>(
                tween: Tween(begin: reduceMotion ? 1 : 0, end: 1),
                duration: const Duration(milliseconds: 900),
                curve: Curves.easeOutCubic,
                builder: (context, t, child) => Opacity(
                  opacity: t.clamp(0, 1),
                  child: Transform.translate(
                    offset: Offset(0, 18 * (1 - t)),
                    child: Transform.rotate(angle: -math.pi / 180 * (1.5 + 4.5 * t), child: child),
                  ),
                ),
                child: const _Sticker(tag: _tag),
              ),
            ),
            Positioned(
              right: 14,
              bottom: 16,
              child: TweenAnimationBuilder<double>(
                tween: Tween(begin: reduceMotion ? 1 : 0, end: 1),
                duration: const Duration(milliseconds: 700),
                curve: const Interval(0.45, 1, curve: Curves.easeOutBack),
                builder: (context, t, child) => Opacity(
                  opacity: t.clamp(0, 1),
                  child: Transform.scale(scale: 0.9 + 0.1 * t, child: child),
                ),
                child: const _OutcomeToast(),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _Sticker extends StatelessWidget {
  const _Sticker({required this.tag});

  final String tag;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Container(
      width: 252,
      padding: const EdgeInsets.fromLTRB(12, 12, 16, 12),
      decoration: BoxDecoration(
        color: MxColors.surface,
        borderRadius: BorderRadius.circular(18),
        boxShadow: mxFloatShadow,
      ),
      child: Row(
        children: [
          Container(
            decoration: BoxDecoration(
              color: MxColors.surface,
              borderRadius: BorderRadius.circular(10),
              border: Border.all(color: MxColors.hairline),
            ),
            child: QrImageView(
              data: tag,
              size: 84,
              padding: const EdgeInsets.all(6),
              eyeStyle: const QrEyeStyle(eyeShape: QrEyeShape.square, color: MxColors.ink),
              dataModuleStyle: const QrDataModuleStyle(
                dataModuleShape: QrDataModuleShape.square,
                color: MxColors.ink,
              ),
            ),
          ),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const MxBrandMark(size: 16),
                const SizedBox(height: 10),
                FittedBox(
                  fit: BoxFit.scaleDown,
                  alignment: Alignment.centerLeft,
                  child: Text(
                    tag,
                    style: theme.textTheme.titleMedium?.copyWith(
                      letterSpacing: -0.3,
                      fontFeatures: MxType.tabular,
                    ),
                  ),
                ),
                const SizedBox(height: 8),
                Text('Lecture Hall A', style: theme.textTheme.bodySmall?.copyWith(fontSize: 12)),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _OutcomeToast extends StatelessWidget {
  const _OutcomeToast();

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Container(
      padding: const EdgeInsets.fromLTRB(8, 8, 14, 8),
      decoration: BoxDecoration(
        color: MxColors.surface,
        borderRadius: BorderRadius.circular(999),
        boxShadow: mxFloatShadow,
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Container(
            width: 26,
            height: 26,
            decoration: const BoxDecoration(color: MxColors.ink, shape: BoxShape.circle),
            child: const Icon(LucideIcons.check, size: 14, color: Colors.white),
          ),
          const SizedBox(width: 8),
          Text('Repair confirmed', style: theme.textTheme.labelMedium),
        ],
      ),
    );
  }
}

/// A quiet grid of dots — QR modules at rest — behind the sticker.
class _DotGridPainter extends CustomPainter {
  const _DotGridPainter();

  @override
  void paint(Canvas canvas, Size size) {
    const gap = 18.0;
    final paint = Paint()..color = const Color(0xFFD9DCE2);
    for (var y = gap / 2; y < size.height; y += gap) {
      for (var x = gap / 2; x < size.width; x += gap) {
        canvas.drawCircle(Offset(x, y), 1.1, paint);
      }
    }
  }

  @override
  bool shouldRepaint(covariant CustomPainter oldDelegate) => false;
}

class _Notice extends StatelessWidget {
  const _Notice({
    required this.title,
    required this.message,
    required this.background,
    required this.border,
    required this.foreground,
    required this.icon,
  });

  final String title;
  final String message;
  final Color background;
  final Color border;
  final Color foreground;
  final IconData icon;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Container(
      margin: const EdgeInsets.only(bottom: 16),
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: background,
        border: Border.all(color: border),
        borderRadius: BorderRadius.circular(MxRadii.md),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(icon, size: 18, color: foreground),
          const SizedBox(width: 10),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(title, style: theme.textTheme.titleSmall?.copyWith(color: foreground)),
                const SizedBox(height: 2),
                Text(message, style: theme.textTheme.bodyMedium?.copyWith(color: foreground)),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
