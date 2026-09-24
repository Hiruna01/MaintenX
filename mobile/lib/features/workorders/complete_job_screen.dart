import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api_client.dart';
import '../../widgets/app_form_field.dart';
import '../../widgets/empty_view.dart';
import '../../widgets/error_view.dart';
import '../../widgets/loading_view.dart';
import '../assets/asset.dart';
import '../reports/report_photo.dart';
import 'completion.dart';
import 'job_detail_screen.dart';
import 'work_order.dart';
import 'work_orders_api.dart';

/// Where the completion has got to. The photo is uploaded FIRST and the job completed
/// second, because completing is the step that cannot be undone: a failed upload leaves the
/// job open, which is its own state rather than a generic error.
enum _Phase { editing, uploading, uploadFailed, completing }

/// Close a job: what the visit came to, what it cost, what was done, and optionally a photo.
///
/// The resolution note becomes the asset's ServiceRecord verbatim, which is what the
/// diagnostic agent reads months later — hence the 20-character floor in [validateCompletion],
/// held by the API as well.
class CompleteJobScreen extends ConsumerStatefulWidget {
  const CompleteJobScreen({super.key, required this.workOrderId});

  /// Nested under the job: `/jobs/7/complete`.
  static const String subPath = 'complete';

  static String location(int id) => '/jobs/$id/complete';

  final int? workOrderId;

  @override
  ConsumerState<CompleteJobScreen> createState() => _CompleteJobScreenState();
}

class _CompleteJobScreenState extends ConsumerState<CompleteJobScreen> {
  final _costController = TextEditingController();
  final _noteController = TextEditingController();

  /// Null until picked — no default outcome, for the reason the API's is `[Required]`.
  String? _outcome;
  Map<String, String> _errors = const {};
  String? _submitError;

  _Phase _phase = _Phase.editing;

  PickedPhoto? _photo;
  String? _photoError;

  /// True once [_photo] is on the order. A retry after a failed completion then does not
  /// upload it again; choosing a different photo clears it.
  bool _photoUploaded = false;

  /// 0..1 while uploading; 1 means every byte has gone and the API is storing it.
  double _uploadProgress = 0;
  String? _uploadError;

  bool get _isEditing => _phase == _Phase.editing;

  @override
  void dispose() {
    _costController.dispose();
    _noteController.dispose();
    super.dispose();
  }

  Future<void> _pickPhoto() async {
    try {
      final photo = await choosePhoto(context, ref.read(imagePickerProvider));
      if (photo != null && mounted) {
        setState(() {
          _photo = photo;
          _photoUploaded = false;
          _photoError = null;
        });
      }
    } on PhotoRejected catch (error) {
      if (mounted) setState(() => _photoError = error.message);
    }
  }

  Future<void> _submit(int id) async {
    final errors = validateCompletion(
      outcome: _outcome,
      actualCost: _costController.text,
      resolutionNote: _noteController.text,
    );
    setState(() {
      _errors = errors;
      _submitError = null;
    });
    if (errors.isNotEmpty) return;

    if (_photo != null && !_photoUploaded) {
      await _uploadPhoto(id);
    } else {
      await _complete(id);
    }
  }

  /// Attaches [_photo] to the job, then completes it. Called again by "Retry upload".
  Future<void> _uploadPhoto(int id) async {
    final photo = _photo!;
    setState(() {
      _phase = _Phase.uploading;
      _uploadProgress = 0;
      _uploadError = null;
    });

    try {
      await ref.read(workOrdersApiProvider).uploadCompletionPhoto(
            id,
            bytes: photo.bytes,
            contentType: photo.contentType,
            onProgress: (sent, total) {
              if (mounted && total > 0) setState(() => _uploadProgress = sent / total);
            },
          );
    } on ApiException catch (error) {
      if (!mounted) return;
      setState(() {
        _phase = _Phase.uploadFailed;
        _uploadError = error.message;
      });
      return;
    }

    if (!mounted) return;
    _photoUploaded = true;
    await _complete(id);
  }

  Future<void> _complete(int id) async {
    setState(() => _phase = _Phase.completing);

    try {
      await ref.read(workOrdersApiProvider).complete(
            id,
            outcome: _outcome!,
            actualCost: _costController.text,
            resolutionNote: _noteController.text,
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
    // Both views of this job are stale now.
    ref.invalidate(workOrderDetailProvider(id));
    ref.invalidate(jobsPageProvider);
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(_photoUploaded ? 'Job completed with photo.' : 'Job completed.')),
    );
    context.go(JobDetailScreen.location(id));
  }

  @override
  Widget build(BuildContext context) {
    final id = widget.workOrderId;
    if (id == null) {
      return Scaffold(
        appBar: AppBar(title: const Text('Complete job')),
        body: const ErrorView(title: 'Not a job', message: 'This link does not point at a job.'),
      );
    }

    final order = ref.watch(workOrderDetailProvider(id));

    return Scaffold(
      appBar: AppBar(title: const Text('Complete job')),
      body: SafeArea(
        child: order.when(
          loading: () => const LoadingView(message: 'Loading job…'),
          error: (error, _) => ErrorView(
            title: 'Could not load this job',
            message: error is ApiException ? error.message : 'Could not reach the API.',
            onRetry: () => ref.invalidate(workOrderDetailProvider(id)),
          ),
          data: (detail) => detail.isCompletable || !_isEditing
              ? _form(detail)
              // Already finished (or not yet approved): nothing to complete, and not an error.
              : EmptyView(
                  icon: Icons.task_alt,
                  message: 'This job is ${WorkOrderStatuses.label(detail.status).toLowerCase()}'
                      ' — there is nothing to complete.',
                  action: TextButton(
                    onPressed: () => context.go(JobDetailScreen.location(id)),
                    child: const Text('Back to the job'),
                  ),
                ),
        ),
      ),
    );
  }

  Widget _form(WorkOrderDetail detail) {
    final theme = Theme.of(context);
    final muted = theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.outline);

    return ListView(
      padding: const EdgeInsets.all(24),
      children: [
        Text('${detail.assetTag} · ${detail.room.code}', style: theme.textTheme.titleMedium),
        Text(detail.assetName, style: muted),
        const SizedBox(height: 20),

        if (_submitError != null) ...[
          Text(_submitError!, style: TextStyle(color: theme.colorScheme.error)),
          const SizedBox(height: 16),
        ],

        AppDropdownField<String>(
          label: 'Outcome',
          value: _outcome,
          errorText: _errors['outcome'],
          hintText: 'What did the visit come to?',
          items: [
            for (final outcome in ServiceOutcomes.all)
              DropdownMenuItem(value: outcome, child: Text(ServiceOutcomes.label(outcome))),
          ],
          onChanged: (value) {
            if (!_isEditing) return;
            setState(() {
              _outcome = value;
              _errors = {..._errors}..remove('outcome');
            });
          },
        ),

        AppFormField(
          label: 'Actual cost (Rs)',
          controller: _costController,
          errorText: _errors['actualCost'],
          hintText: 'e.g. 1500 or 1500.50',
          keyboardType: const TextInputType.numberWithOptions(decimal: true),
          enabled: _isEditing,
        ),

        Padding(
          padding: const EdgeInsets.only(bottom: 8),
          child: Text(
            'Write what was wrong and what you did. This note is saved word for word in the '
            "asset's service history, where the next diagnosis reads it — at least "
            '$minResolutionNoteLength characters.',
            style: muted,
          ),
        ),
        AppFormField(
          label: 'Resolution note',
          controller: _noteController,
          errorText: _errors['resolutionNote'],
          hintText: 'e.g. fan bearing worn, replaced fan + cleaned filter. ran 30min, no cutout.',
          maxLines: 6,
          maxLength: maxResolutionNoteLength,
          enabled: _isEditing,
        ),

        if (_photo != null) _PhotoPreview(photo: _photo!, uploaded: _photoUploaded),
        if (_photoError != null)
          Padding(
            padding: const EdgeInsets.only(bottom: 8),
            child: Text(_photoError!, style: TextStyle(color: theme.colorScheme.error)),
          ),
        OutlinedButton.icon(
          // A photo can be changed after a failed upload too — a 400 for the file itself is
          // fixed by choosing a different one, not by retrying this one.
          onPressed: _isEditing || _phase == _Phase.uploadFailed ? _pickPhoto : null,
          icon: const Icon(Icons.photo_camera_outlined),
          label: Text(_photo == null ? 'Add completion photo' : 'Change photo'),
        ),
        const SizedBox(height: 4),
        Text('Optional — evidence the work was done.', style: muted),

        const SizedBox(height: 24),
        ..._actions(detail.id, theme),
      ],
    );
  }

  List<Widget> _actions(int id, ThemeData theme) {
    switch (_phase) {
      case _Phase.editing:
        return [FilledButton(onPressed: () => _submit(id), child: const Text('Complete job'))];

      case _Phase.completing:
        return [const FilledButton(onPressed: null, child: Text('Completing…'))];

      case _Phase.uploading:
        final percent = (_uploadProgress * 100).round();
        return [
          LinearProgressIndicator(value: _uploadProgress < 1 ? _uploadProgress : null),
          const SizedBox(height: 8),
          Text(
            _uploadProgress < 1 ? 'Uploading photo… $percent%' : 'Saving photo…',
            style: theme.textTheme.bodyMedium,
          ),
        ];

      case _Phase.uploadFailed:
        return [
          // The job is NOT completed — said first, so nobody walks away thinking it is.
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
                    'The job has not been completed yet — the photo could not be uploaded. '
                    '${_uploadError ?? ''}',
                    style: TextStyle(color: theme.colorScheme.onErrorContainer),
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(height: 12),
          FilledButton(onPressed: () => _uploadPhoto(id), child: const Text('Retry upload')),
          const SizedBox(height: 8),
          OutlinedButton(
            onPressed: () {
              setState(() => _photo = null);
              _complete(id);
            },
            child: const Text('Complete without photo'),
          ),
        ];
    }
  }
}

class _PhotoPreview extends StatelessWidget {
  const _PhotoPreview({required this.photo, required this.uploaded});

  final PickedPhoto photo;
  final bool uploaded;

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
              'Photo · ${photo.contentType == 'image/png' ? 'PNG' : 'JPEG'} · ${photo.sizeLabel}'
              '${uploaded ? '\nAttached to the job' : ''}',
              style: theme.textTheme.bodyMedium,
            ),
          ),
        ],
      ),
    );
  }
}
