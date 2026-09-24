import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api_client.dart';
import '../../widgets/app_form_field.dart';
import '../../widgets/empty_view.dart';
import '../../widgets/error_view.dart';
import '../../widgets/loading_view.dart';
import 'my_reports_screen.dart';
import 'report_photo.dart';
import 'reports_api.dart';
import 'room.dart';

/// Where the submission has got to. A photo is attached to a report that already exists,
/// so filing and uploading are two requests — and the second can fail after the first has
/// succeeded, which is its own state rather than a generic error.
enum _Phase { editing, filing, uploading, uploadFailed }

/// Submit a maintenance report: what is wrong, where, and optionally a photo of it.
///
/// There is no chat interface here and there will not be one. When the agent needs more
/// detail, its clarification questions are rendered as another FORM with bounded inputs —
/// pickers, yes/no, short text — never as a message thread. See ClarificationScreen.
class SubmitReportScreen extends ConsumerStatefulWidget {
  const SubmitReportScreen({super.key});

  static const String subPath = 'report';
  static const String path = '/report';

  @override
  ConsumerState<SubmitReportScreen> createState() => _SubmitReportScreenState();
}

class _SubmitReportScreenState extends ConsumerState<SubmitReportScreen> {
  final _descriptionController = TextEditingController();

  int? _roomId;
  Map<String, String> _errors = const {};
  String? _submitError;

  _Phase _phase = _Phase.editing;

  PickedPhoto? _photo;
  String? _photoError;

  /// Set once the report is filed. From then on the form is fixed, and a retry uploads the
  /// photo again without filing a second report.
  int? _reportId;

  /// 0..1 while uploading; 1 means every byte has gone and the API is storing it.
  double _uploadProgress = 0;
  String? _uploadError;

  bool get _isEditing => _phase == _Phase.editing;

  @override
  void dispose() {
    _descriptionController.dispose();
    super.dispose();
  }

  /// Returns a message per invalid field. An empty map means the form is valid.
  Map<String, String> _validate() {
    final errors = <String, String>{};
    final description = _descriptionController.text.trim();

    if (description.isEmpty) {
      errors['description'] = 'Describe what is wrong.';
    } else if (description.length < 10) {
      errors['description'] = 'Please give a little more detail (at least 10 characters).';
    }

    if (_roomId == null) {
      errors['room'] = 'Choose the room.';
    }

    return errors;
  }

  Future<void> _pickPhoto() async {
    try {
      final photo = await choosePhoto(context, ref.read(imagePickerProvider));
      if (photo != null && mounted) {
        setState(() {
          _photo = photo;
          _photoError = null;
        });
      }
    } on PhotoRejected catch (error) {
      if (mounted) setState(() => _photoError = error.message);
    }
  }

  Future<void> _submit() async {
    final errors = _validate();
    setState(() {
      _errors = errors;
      _submitError = null;
    });

    if (errors.isNotEmpty) return;

    setState(() => _phase = _Phase.filing);

    try {
      _reportId = await ref.read(reportsApiProvider).submit(
            description: _descriptionController.text.trim(),
            roomId: _roomId!,
          );
    } on ApiException catch (error) {
      if (mounted) {
        setState(() {
          _submitError = error.message;
          _phase = _Phase.editing;
        });
      }
      return;
    }

    if (!mounted) return;
    if (_photo == null) {
      _finish('Report submitted.');
    } else {
      await _uploadPhoto();
    }
  }

  /// Uploads [_photo] to the report already filed. Called again by "Retry upload", which
  /// is why it never files anything itself.
  Future<void> _uploadPhoto() async {
    final photo = _photo!;
    setState(() {
      _phase = _Phase.uploading;
      _uploadProgress = 0;
      _uploadError = null;
    });

    try {
      await ref.read(reportsApiProvider).uploadPhoto(
            _reportId!,
            bytes: photo.bytes,
            contentType: photo.contentType,
            onProgress: (sent, total) {
              if (mounted && total > 0) setState(() => _uploadProgress = sent / total);
            },
          );
      if (mounted) _finish('Report submitted with photo.');
    } on ApiException catch (error) {
      if (!mounted) return;
      setState(() {
        _phase = _Phase.uploadFailed;
        _uploadError = error.message;
      });
    }
  }

  void _finish(String message) {
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(message)));
    // Straight to the list, where the agent's questions will appear once it has read the
    // report.
    context.go(MyReportsScreen.path);
  }

  @override
  Widget build(BuildContext context) {
    final rooms = ref.watch(roomsProvider);

    return Scaffold(
      appBar: AppBar(title: const Text('Submit a report')),
      body: SafeArea(
        // All three states of the room request are rendered explicitly.
        child: rooms.when(
          loading: () => const LoadingView(message: 'Loading rooms…'),
          error: (error, _) => ErrorView(
            title: 'Could not load rooms',
            message: error is ApiException ? error.message : 'Could not reach the API.',
            onRetry: () => ref.invalidate(roomsProvider),
          ),
          data: (data) => data.isEmpty
              ? const EmptyView(
                  message: 'No rooms have been added yet, so there is nothing to report against.',
                )
              : _form(data),
        ),
      ),
    );
  }

  Widget _form(List<Room> rooms) {
    final theme = Theme.of(context);

    return ListView(
      padding: const EdgeInsets.all(24),
      children: [
        if (_submitError != null) ...[
          Text(
            _submitError!,
            style: TextStyle(color: theme.colorScheme.error),
          ),
          const SizedBox(height: 16),
        ],

        AppFormField(
          label: 'What is wrong?',
          controller: _descriptionController,
          errorText: _errors['description'],
          hintText: 'e.g. the projector in this room will not power on',
          maxLines: 4,
          enabled: _isEditing,
        ),

        AppDropdownField<int>(
          label: 'Room',
          value: _roomId,
          errorText: _errors['room'],
          hintText: 'Choose a room',
          items: rooms
              .map((room) => DropdownMenuItem(value: room.id, child: Text(room.label)))
              .toList(growable: false),
          onChanged: (value) {
            if (!_isEditing) return;
            setState(() {
              _roomId = value;
              _errors = {..._errors}..remove('room');
            });
          },
        ),

        if (_photo != null)
          _PhotoPreview(
            photo: _photo!,
            // Removing is for before the report is filed. After an upload failure,
            // "Continue without photo" is the way to drop it.
            onRemove: _isEditing ? () => setState(() => _photo = null) : null,
          ),
        if (_photoError != null)
          Padding(
            padding: const EdgeInsets.only(bottom: 8),
            child: Text(_photoError!, style: TextStyle(color: theme.colorScheme.error)),
          ),

        const SizedBox(height: 8),
        Row(
          children: [
            Expanded(
              child: OutlinedButton.icon(
                // A photo can be changed after an upload failure too — a 400 for the file
                // itself is fixed by choosing a different one, not by retrying this one.
                onPressed: _isEditing || _phase == _Phase.uploadFailed ? _pickPhoto : null,
                icon: const Icon(Icons.photo_camera_outlined),
                label: Text(_photo == null ? 'Add photo' : 'Change photo'),
              ),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: OutlinedButton.icon(
                // TODO(qr): scan an asset code and preselect its room.
                onPressed: null,
                icon: const Icon(Icons.qr_code_scanner),
                label: const Text('Scan QR'),
              ),
            ),
          ],
        ),
        const SizedBox(height: 8),
        Text(
          'QR scanning is coming soon.',
          style: theme.textTheme.bodySmall,
        ),

        const SizedBox(height: 24),
        ..._actions(theme),
      ],
    );
  }

  List<Widget> _actions(ThemeData theme) {
    switch (_phase) {
      case _Phase.editing:
        return [FilledButton(onPressed: _submit, child: const Text('Submit report'))];

      case _Phase.filing:
        return [const FilledButton(onPressed: null, child: Text('Submitting…'))];

      case _Phase.uploading:
        final percent = (_uploadProgress * 100).round();
        return [
          // Determinate while bytes are going out; indeterminate once they have all gone
          // and the API is putting the file into storage before it answers.
          LinearProgressIndicator(value: _uploadProgress < 1 ? _uploadProgress : null),
          const SizedBox(height: 8),
          Text(
            _uploadProgress < 1 ? 'Uploading photo… $percent%' : 'Saving photo…',
            style: theme.textTheme.bodyMedium,
          ),
        ];

      case _Phase.uploadFailed:
        return [
          // The report IS filed — said first, so nobody files it a second time.
          Container(
            padding: const EdgeInsets.all(12),
            decoration: BoxDecoration(
              color: theme.colorScheme.errorContainer,
              borderRadius: BorderRadius.circular(8),
            ),
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Icon(Icons.error_outline, color: theme.colorScheme.onErrorContainer),
                const SizedBox(width: 8),
                Expanded(
                  child: Text(
                    'Your report has been filed, but the photo was not attached. '
                    '${_uploadError ?? ''}',
                    style: TextStyle(color: theme.colorScheme.onErrorContainer),
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(height: 12),
          FilledButton(onPressed: _uploadPhoto, child: const Text('Retry upload')),
          const SizedBox(height: 8),
          OutlinedButton(
            onPressed: () => _finish('Report submitted without a photo.'),
            child: const Text('Continue without photo'),
          ),
        ];
    }
  }
}

class _PhotoPreview extends StatelessWidget {
  const _PhotoPreview({required this.photo, required this.onRemove});

  final PickedPhoto photo;
  final VoidCallback? onRemove;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: Row(
        children: [
          ClipRRect(
            borderRadius: BorderRadius.circular(8),
            child: Image.memory(
              photo.bytes,
              width: 72,
              height: 72,
              fit: BoxFit.cover,
              // A photo the phone cannot preview still uploads — the API is what judges it.
              errorBuilder: (context, _, __) => Container(
                width: 72,
                height: 72,
                color: theme.colorScheme.surfaceContainerHighest,
                child: Icon(Icons.image_outlined, color: theme.colorScheme.outline),
              ),
            ),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Text(
              'Photo · ${photo.contentType == 'image/png' ? 'PNG' : 'JPEG'} · '
              '${photo.sizeLabel}',
              style: theme.textTheme.bodyMedium,
            ),
          ),
          if (onRemove != null)
            IconButton(
              tooltip: 'Remove photo',
              icon: const Icon(Icons.close),
              onPressed: onRemove,
            ),
        ],
      ),
    );
  }
}
