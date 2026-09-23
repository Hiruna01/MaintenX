import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:mobile_scanner/mobile_scanner.dart';

import '../../core/api_client.dart';
import '../../widgets/error_view.dart';
import '../../widgets/loading_view.dart';
import 'asset_detail_screen.dart';
import 'assets_api.dart';

/// Where the scanner is. The camera stays in the tree the whole time; everything but
/// [scanning] is drawn over it, and a detection that arrives in any other phase is ignored,
/// so one sticker held in view produces exactly one lookup.
enum _Phase { scanning, lookingUp, notFound, failed }

/// Point the camera at an asset's QR sticker and land on that asset.
///
/// The QR code carries the asset tag and nothing else — `Asset.AssetTag` is the QR payload,
/// unique across the estate — so a scan is exactly one call:
/// GET /api/assets/by-tag/{assetTag}.
///
/// Every way this can go wrong in front of an audience has its own visible state rather
/// than a blank screen or a crash:
///   * camera permission denied -> an explanation and a retry ([_CameraErrorView])
///   * a code that is not one of ours -> "No asset registered for this code"
///   * no network, timeout, server error -> ErrorView with a retry
/// and a typed-tag fallback for a damaged sticker, or a device with no usable camera.
class ScanAssetScreen extends ConsumerStatefulWidget {
  const ScanAssetScreen({super.key});

  static const String subPath = 'scan';
  static const String path = '/scan';

  @override
  ConsumerState<ScanAssetScreen> createState() => _ScanAssetScreenState();
}

class _ScanAssetScreenState extends ConsumerState<ScanAssetScreen> {
  // QR only: the stickers are QR codes, and reading every 1D barcode on a lecture-hall
  // wall would turn a poster's ISBN into a "No asset registered" result.
  final _controller = MobileScannerController(formats: const [BarcodeFormat.qrCode]);

  _Phase _phase = _Phase.scanning;
  String? _scannedTag;
  String? _failureMessage;

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  void _onDetect(BarcodeCapture capture) {
    if (_phase != _Phase.scanning) return;

    for (final barcode in capture.barcodes) {
      final value = barcode.rawValue?.trim();
      if (value != null && value.isNotEmpty) {
        _lookUp(value);
        return;
      }
    }
  }

  Future<void> _lookUp(String tag) async {
    setState(() {
      _phase = _Phase.lookingUp;
      _scannedTag = tag;
      _failureMessage = null;
    });

    try {
      final asset = await ref.read(assetsApiProvider).findByTag(tag);
      if (!mounted) return;

      if (asset == null) {
        setState(() => _phase = _Phase.notFound);
        return;
      }

      // Release the camera while the detail screen is on top, and take it back when the
      // user returns — otherwise it keeps running, and scanning, behind a screen they
      // cannot see.
      await _controller.stop();
      if (!mounted) return;
      await context.push(AssetDetailScreen.location(asset.id));
      if (!mounted) return;
      _scanAgain();
    } on ApiException catch (error) {
      // A 401 never lands here as a problem to show: ApiClient has already cleared the
      // token, and the router's guard is taking the user to login.
      if (!mounted) return;
      setState(() {
        _phase = _Phase.failed;
        _failureMessage = error.message;
      });
    }
  }

  Future<void> _scanAgain() async {
    setState(() {
      _phase = _Phase.scanning;
      _scannedTag = null;
      _failureMessage = null;
    });
    await _restartCamera();
  }

  Future<void> _restartCamera() async {
    try {
      await _controller.start();
    } on MobileScannerException {
      // start() records a failed start on the controller itself, which the MobileScanner
      // errorBuilder renders; the only throws left are "already starting" and "disposed",
      // and both are harmless here.
    }
  }

  Future<void> _enterTagManually() async {
    final tag = await showDialog<String>(
      context: context,
      builder: (context) => const _ManualTagDialog(),
    );
    if (tag != null && mounted) {
      _lookUp(tag);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Scan an asset'),
        actions: [
          IconButton(
            tooltip: 'Type the tag instead',
            icon: const Icon(Icons.keyboard_outlined),
            onPressed: _phase == _Phase.lookingUp ? null : _enterTagManually,
          ),
        ],
      ),
      body: Stack(
        fit: StackFit.expand,
        children: [
          MobileScanner(
            controller: _controller,
            onDetect: _onDetect,
            // Never a black rectangle while the camera starts.
            placeholderBuilder: (context) => const ColoredBox(
              color: Colors.black,
              child: Center(child: CircularProgressIndicator(color: Colors.white)),
            ),
            errorBuilder: (context, error) => _CameraErrorView(
              error: error,
              onRetry: _restartCamera,
              onEnterManually: _enterTagManually,
            ),
          ),
          if (_phase == _Phase.scanning || _phase == _Phase.lookingUp)
            // The controller is a ValueNotifier of the camera's state. With the camera in
            // error the frame would sit on top of the error message, so a typed lookup
            // gets a plain loading panel instead.
            ValueListenableBuilder<MobileScannerState>(
              valueListenable: _controller,
              builder: (context, camera, _) {
                if (camera.error == null) {
                  return _ViewfinderOverlay(
                    lookingUpTag: _phase == _Phase.lookingUp ? _scannedTag : null,
                  );
                }
                return _phase == _Phase.lookingUp
                    ? _Panel(child: LoadingView(message: 'Looking up $_scannedTag…'))
                    : const SizedBox.shrink();
              },
            ),
          if (_phase == _Phase.notFound)
            _Panel(
              child: _NotFoundView(tag: _scannedTag ?? '', onScanAgain: _scanAgain),
            ),
          if (_phase == _Phase.failed)
            _Panel(
              child: Column(
                children: [
                  Expanded(
                    child: ErrorView(
                      title: 'Could not look up this code',
                      message: _failureMessage ?? 'Could not reach the API.',
                      onRetry: () => _lookUp(_scannedTag!),
                    ),
                  ),
                  Padding(
                    padding: const EdgeInsets.only(bottom: 24),
                    child: TextButton(
                      onPressed: _scanAgain,
                      child: const Text('Scan a different code'),
                    ),
                  ),
                ],
              ),
            ),
        ],
      ),
    );
  }
}

/// An opaque sheet over the camera, so a result is never read against a moving picture.
class _Panel extends StatelessWidget {
  const _Panel({required this.child});

  final Widget child;

  @override
  Widget build(BuildContext context) {
    return ColoredBox(
      color: Theme.of(context).colorScheme.surface,
      child: SafeArea(child: child),
    );
  }
}

/// The framing square and the instruction under it — or, while a lookup is in flight, the
/// tag that was read, so the user can see the scan landed.
class _ViewfinderOverlay extends StatelessWidget {
  const _ViewfinderOverlay({this.lookingUpTag});

  final String? lookingUpTag;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return IgnorePointer(
      child: Column(
        children: [
          const Spacer(),
          Container(
            width: 240,
            height: 240,
            decoration: BoxDecoration(
              border: Border.all(color: Colors.white, width: 3),
              borderRadius: BorderRadius.circular(24),
            ),
          ),
          const Spacer(),
          SafeArea(
            top: false,
            child: Padding(
              padding: const EdgeInsets.fromLTRB(24, 0, 24, 32),
              child: Container(
                padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
                decoration: BoxDecoration(
                  color: Colors.black.withValues(alpha: 0.65),
                  borderRadius: BorderRadius.circular(12),
                ),
                child: lookingUpTag == null
                    ? Text(
                        "Point the camera at an asset's QR sticker",
                        textAlign: TextAlign.center,
                        style: theme.textTheme.bodyMedium?.copyWith(color: Colors.white),
                      )
                    : Row(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          const SizedBox(
                            width: 18,
                            height: 18,
                            child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white),
                          ),
                          const SizedBox(width: 12),
                          Flexible(
                            child: Text(
                              'Looking up $lookingUpTag…',
                              style: theme.textTheme.bodyMedium?.copyWith(color: Colors.white),
                            ),
                          ),
                        ],
                      ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

/// A 404 from the QR path. An unknown code is an ordinary answer, not a failure: the
/// sticker is from some other system, or the asset was never registered here.
class _NotFoundView extends StatelessWidget {
  const _NotFoundView({required this.tag, required this.onScanAgain});

  final String tag;
  final VoidCallback onScanAgain;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Center(
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(Icons.qr_code_2, size: 48, color: theme.colorScheme.outline),
            const SizedBox(height: 12),
            Text(
              'No asset registered for this code',
              textAlign: TextAlign.center,
              style: theme.textTheme.titleMedium,
            ),
            const SizedBox(height: 8),
            Text(
              'The code read as:',
              style: theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.outline),
            ),
            const SizedBox(height: 4),
            // Shown exactly as read, so a mis-scan and a foreign sticker can be told apart.
            SelectableText(
              tag,
              textAlign: TextAlign.center,
              style: theme.textTheme.bodyMedium?.copyWith(fontFamily: 'monospace'),
            ),
            const SizedBox(height: 20),
            FilledButton.icon(
              onPressed: onScanAgain,
              icon: const Icon(Icons.qr_code_scanner),
              label: const Text('Scan again'),
            ),
          ],
        ),
      ),
    );
  }
}

/// What MobileScanner shows when the camera cannot start. Permission denied is the one a
/// demo actually hits, so it gets its own words; every case gets a retry and the typed-tag
/// fallback, and none of them is a blank screen.
class _CameraErrorView extends StatelessWidget {
  const _CameraErrorView({
    required this.error,
    required this.onRetry,
    required this.onEnterManually,
  });

  final MobileScannerException error;
  final VoidCallback onRetry;
  final VoidCallback onEnterManually;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    final (IconData icon, String title, String message) = switch (error.errorCode) {
      MobileScannerErrorCode.permissionDenied => (
          Icons.no_photography_outlined,
          'Camera access is off',
          'MaintenX needs the camera to read asset stickers. Allow camera access for '
              'MaintenX in your phone\'s Settings, then try again.',
        ),
      MobileScannerErrorCode.unsupported => (
          Icons.videocam_off_outlined,
          'No camera available',
          'This device has no camera the scanner can use. You can type the asset tag instead.',
        ),
      _ => (
          Icons.videocam_off_outlined,
          'The camera could not start',
          error.errorDetails?.message ?? 'Something went wrong starting the camera.',
        ),
    };

    return ColoredBox(
      color: theme.colorScheme.surface,
      child: Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(icon, size: 48, color: theme.colorScheme.error),
              const SizedBox(height: 12),
              Text(title, style: theme.textTheme.titleMedium, textAlign: TextAlign.center),
              const SizedBox(height: 8),
              Text(message, style: theme.textTheme.bodyMedium, textAlign: TextAlign.center),
              const SizedBox(height: 20),
              FilledButton.tonal(onPressed: onRetry, child: const Text('Try again')),
              const SizedBox(height: 8),
              TextButton(onPressed: onEnterManually, child: const Text('Type the tag instead')),
            ],
          ),
        ),
      ),
    );
  }
}

/// The typed fallback: one bounded field, capped at the API's 50-character tag limit.
/// It goes down the same by-tag lookup as a scan, so it gets the same three outcomes.
class _ManualTagDialog extends StatefulWidget {
  const _ManualTagDialog();

  @override
  State<_ManualTagDialog> createState() => _ManualTagDialogState();
}

class _ManualTagDialogState extends State<_ManualTagDialog> {
  static const int _maxTagLength = 50;

  final _controller = TextEditingController();
  Map<String, String> _errors = const {};

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  /// Returns a message per invalid field — the same shape every form in the app uses.
  Map<String, String> _validate() {
    final tag = _controller.text.trim();
    if (tag.isEmpty) return {'tag': 'Enter the tag printed under the QR code.'};
    if (tag.length > _maxTagLength) {
      return {'tag': 'An asset tag is at most $_maxTagLength characters.'};
    }
    return const {};
  }

  void _submit() {
    final errors = _validate();
    if (errors.isNotEmpty) {
      setState(() => _errors = errors);
      return;
    }
    Navigator.of(context).pop(_controller.text.trim());
  }

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      title: const Text('Type the asset tag'),
      content: TextField(
        controller: _controller,
        autofocus: true,
        maxLength: _maxTagLength,
        textCapitalization: TextCapitalization.characters,
        autocorrect: false,
        onSubmitted: (_) => _submit(),
        onChanged: (_) {
          if (_errors.isNotEmpty) setState(() => _errors = const {});
        },
        decoration: InputDecoration(
          hintText: 'e.g. PRJ-MAB101-01',
          errorText: _errors['tag'],
          border: const OutlineInputBorder(),
        ),
      ),
      actions: [
        TextButton(onPressed: () => Navigator.of(context).pop(), child: const Text('Cancel')),
        FilledButton(onPressed: _submit, child: const Text('Look up')),
      ],
    );
  }
}
