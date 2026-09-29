import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/api_client.dart';
import '../../core/app_theme.dart';
import '../../widgets/app_form_field.dart';
import '../../widgets/empty_view.dart';
import '../../widgets/error_view.dart';
import '../../widgets/loading_view.dart';
import '../../widgets/surfaces.dart';
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
    final appBar = AppBar(
      leading: Navigator.canPop(context)
          ? IconButton(
              tooltip: 'Back',
              icon: const Icon(LucideIcons.arrowLeft),
              onPressed: () => Navigator.maybePop(context),
            )
          : null,
    );
    if (id == null) {
      return Scaffold(
        backgroundColor: MxColors.surface,
        appBar: appBar,
        body: const ErrorView(title: 'Not a job', message: 'This link does not point at a job.'),
      );
    }

    final order = ref.watch(workOrderDetailProvider(id));

    return Scaffold(
      backgroundColor: MxColors.surface,
      appBar: appBar,
      body: SafeArea(
        top: false,
        child: order.when(
          loading: () => const LoadingView(message: 'Loading job…'),
          error: (error, _) => ErrorView(
            title: 'Could not load this job',
            message: error is ApiException ? error.message : 'Could not reach the API.',
            onRetry: () => ref.invalidate(workOrderDetailProvider(id)),
          ),
          data: (detail) => detail.isCompletable || !_isEditing
              ? Column(
                  children: [
                    Expanded(child: _form(detail)),
                    MxActionBar(children: _actions(detail.id, Theme.of(context))),
                  ],
                )
              // Already finished (or not yet approved): nothing to complete, and not an error.
              : EmptyView(
                  icon: LucideIcons.circleCheck,
                  message: 'This job is ${WorkOrderStatuses.label(detail.status).toLowerCase()}'
                      ' — there is nothing to complete.',
                  action: OutlinedButton(
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
    final help = theme.textTheme.bodySmall;

    return ListView(
      keyboardDismissBehavior: ScrollViewKeyboardDismissBehavior.onDrag,
      padding: const EdgeInsets.fromLTRB(20, 0, 20, 24),
      children: [
        Text('Close the job', style: theme.textTheme.headlineMedium),
        const SizedBox(height: 6),
        Text(
          'What the visit came to, what it cost and what you did. This cannot be undone.',
          style: theme.textTheme.bodyMedium?.copyWith(color: MxColors.graphite),
        ),
        const SizedBox(height: 18),

        // The machine this is for, so nobody closes the wrong job.
        MxWell(
          radius: MxRadii.lg,
          padding: const EdgeInsets.all(14),
          child: Row(
            children: [
              const MxIconTile(icon: LucideIcons.qrCode, background: MxColors.surface),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      detail.assetTag,
                      style: theme.textTheme.titleMedium?.copyWith(fontFeatures: MxType.tabular),
                    ),
                    Text('${detail.assetName}, ${detail.room.code}', style: help),
                  ],
                ),
              ),
            ],
          ),
        ),
        const SizedBox(height: 22),

        if (_submitError != null) ...[
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Padding(
                padding: EdgeInsets.only(top: 2),
                child: Icon(LucideIcons.circleAlert, size: 16, color: MxColors.red),
              ),
              const SizedBox(width: 8),
              Expanded(
                child: Text(
                  _submitError!,
                  style: theme.textTheme.bodyMedium?.copyWith(color: MxColors.red),
                ),
              ),
            ],
          ),
          const SizedBox(height: 14),
        ],

        const MxPanelLabel('Outcome'),
        const SizedBox(height: 8),
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
        const SizedBox(height: 6),

        const MxPanelLabel('Cost'),
        const SizedBox(height: 8),
        AppFormField(
          label: 'Actual cost (Rs)',
          controller: _costController,
          errorText: _errors['actualCost'],
          hintText: 'e.g. 1500 or 1500.50',
          keyboardType: const TextInputType.numberWithOptions(decimal: true),
          enabled: _isEditing,
        ),
        const SizedBox(height: 6),

        const MxPanelLabel('What you did'),
        const SizedBox(height: 6),
        Padding(
          padding: const EdgeInsets.only(bottom: 10),
          child: Text(
            'Write what was wrong and what you did. This note is saved word for word in the '
            "asset's service history, where the next diagnosis reads it — at least "
            '$minResolutionNoteLength characters.',
            style: help,
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
        const SizedBox(height: 10),

        // A photo can be changed after a failed upload too — a 400 for the file itself is
        // fixed by choosing a different one, not by retrying this one.
        if (_photo == null)
          MxDashedTile(
            icon: LucideIcons.camera,
            title: 'Add completion photo',
            subtitle: 'Optional. Evidence the work was done.',
            onTap: _isEditing || _phase == _Phase.uploadFailed ? _pickPhoto : null,
          )
        else
          _PhotoPreview(
            photo: _photo!,
            uploaded: _photoUploaded,
            onChange: _isEditing || _phase == _Phase.uploadFailed ? _pickPhoto : null,
          ),
        if (_photoError != null) ...[
          const SizedBox(height: 10),
          Text(_photoError!, style: theme.textTheme.bodyMedium?.copyWith(color: MxColors.red)),
        ],
      ],
    );
  }

  List<Widget> _actions(int id, ThemeData theme) {
    switch (_phase) {
      case _Phase.editing:
        return [
          FilledButton(
            onPressed: () => _submit(id),
            child: const Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                Icon(LucideIcons.circleCheck, size: 18),
                SizedBox(width: 8),
                Text('Complete job'),
              ],
            ),
          ),
        ];

      case _Phase.completing:
        return [
          FilledButton(
            onPressed: null,
            style: FilledButton.styleFrom(
              disabledBackgroundColor: MxColors.ink2,
              disabledForegroundColor: Colors.white,
            ),
            child: const Row(
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
                Text('Completing…'),
              ],
            ),
          ),
        ];

      case _Phase.uploading:
        final percent = (_uploadProgress * 100).round();
        return [
          MxWell(
            radius: MxRadii.lg,
            padding: const EdgeInsets.fromLTRB(16, 14, 16, 16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Row(
                  children: [
                    const Icon(LucideIcons.cloudUpload, size: 18),
                    const SizedBox(width: 10),
                    Text(
                      _uploadProgress < 1 ? 'Uploading photo… $percent%' : 'Saving photo…',
                      style: theme.textTheme.titleSmall?.copyWith(fontFeatures: MxType.tabular),
                    ),
                  ],
                ),
                const SizedBox(height: 12),
                ClipRRect(
                  borderRadius: BorderRadius.circular(99),
                  child: LinearProgressIndicator(
                    minHeight: 6,
                    value: _uploadProgress < 1 ? _uploadProgress : null,
                  ),
                ),
              ],
            ),
          ),
        ];

      case _Phase.uploadFailed:
        return [
          // The job is NOT completed — said first, so nobody walks away thinking it is.
          Container(
            padding: const EdgeInsets.all(14),
            decoration: BoxDecoration(
              color: MxColors.redBg,
              border: Border.all(color: MxColors.redLine),
              borderRadius: BorderRadius.circular(MxRadii.md),
            ),
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const Icon(LucideIcons.imageOff, size: 18, color: MxColors.red),
                const SizedBox(width: 10),
                Expanded(
                  child: Text(
                    'The job has not been completed yet — the photo could not be uploaded. '
                    '${_uploadError ?? ''}',
                    style: theme.textTheme.bodyMedium?.copyWith(color: MxColors.red),
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(height: 12),
          Row(
            children: [
              Expanded(
                child: OutlinedButton(
                  onPressed: () {
                    setState(() => _photo = null);
                    _complete(id);
                  },
                  child: const Text('Complete without photo'),
                ),
              ),
              const SizedBox(width: 10),
              Expanded(
                child: FilledButton(
                  onPressed: () => _uploadPhoto(id),
                  style: FilledButton.styleFrom(minimumSize: const Size(0, 50)),
                  child: const Text('Retry upload'),
                ),
              ),
            ],
          ),
        ];
    }
  }
}

class _PhotoPreview extends StatelessWidget {
  const _PhotoPreview({required this.photo, required this.uploaded, required this.onChange});

  final PickedPhoto photo;
  final bool uploaded;
  final VoidCallback? onChange;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return MxWell(
      radius: MxRadii.lg,
      padding: const EdgeInsets.all(12),
      child: Row(
        children: [
          ClipRRect(
            borderRadius: BorderRadius.circular(MxRadii.md),
            child: Image.memory(
              photo.bytes,
              width: 72,
              height: 72,
              fit: BoxFit.cover,
              errorBuilder: (context, _, __) => Container(
                width: 72,
                height: 72,
                color: MxColors.wellDeep,
                child: const Icon(LucideIcons.image, color: MxColors.mute),
              ),
            ),
          ),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  '${photo.contentType == 'image/png' ? 'PNG' : 'JPEG'}, ${photo.sizeLabel}',
                  style: theme.textTheme.titleSmall?.copyWith(fontFeatures: MxType.tabular),
                ),
                if (uploaded)
                  Text('Attached to the job', style: theme.textTheme.bodySmall),
              ],
            ),
          ),
          if (onChange != null)
            TextButton(onPressed: onChange, child: const Text('Change photo')),
        ],
      ),
    );
  }
}
