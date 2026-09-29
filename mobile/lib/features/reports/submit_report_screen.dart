import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/api_client.dart';
import '../../core/app_theme.dart';
import '../../widgets/empty_view.dart';
import '../../widgets/error_view.dart';
import '../../widgets/loading_view.dart';
import '../../widgets/surfaces.dart';
import '../assets/asset.dart';
import '../assets/scan_asset_screen.dart';
import 'clarification_screen.dart';
import 'report_photo.dart';
import 'reports_api.dart';
import 'room.dart';

/// Where the submission has got to. A photo is attached to a report that already exists,
/// so filing and uploading are two requests — and the second can fail after the first has
/// succeeded, which is its own state rather than a generic error.
enum _Phase { editing, filing, uploading, uploadFailed }

/// Submit a maintenance report: what is wrong, where, and optionally a photo of it and the
/// equipment, from its scanned sticker.
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

  /// The equipment, when the reporter scanned its sticker. Optional: most reporters will not
  /// know or scan it, and the report is complete without it. Always in [_roomId] — the room
  /// is taken from it, and choosing a different room drops it.
  AssetDetail? _asset;

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

  Future<void> _chooseRoom(List<Room> rooms) async {
    final chosen = await showModalBottomSheet<int>(
      context: context,
      isScrollControlled: true,
      builder: (context) => _RoomSheet(rooms: rooms, selectedId: _roomId),
    );
    if (chosen == null || !mounted || !_isEditing) return;
    setState(() {
      _roomId = chosen;
      // The API refuses an asset that is not in the report's room, so a different room
      // means the scanned machine no longer belongs on this report.
      if (_asset != null && _asset!.room.id != chosen) _asset = null;
      _errors = {..._errors}..remove('room');
    });
  }

  /// Opens the scanner; it comes back with the asset the sticker names, or nothing.
  Future<void> _scanAsset() async {
    final asset = await context.push<AssetDetail>(ScanAssetScreen.reportPath);
    if (asset == null || !mounted || !_isEditing) return;
    setState(() {
      _asset = asset;
      _roomId = asset.room.id;
      _errors = {..._errors}..remove('room');
    });
  }

  Future<void> _submit() async {
    FocusScope.of(context).unfocus();
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
            assetId: _asset?.id,
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
    // Straight to this report's questions. The clarifier is still reading it in the
    // background, so the screen waits for it and opens the form the moment the questions
    // exist — the reporter is never sent to a list to pull-to-refresh for them.
    context.go(ClarificationScreen.location(_reportId!, waitForQuestions: true));
  }

  @override
  Widget build(BuildContext context) {
    final rooms = ref.watch(roomsProvider);

    return Scaffold(
      backgroundColor: MxColors.surface,
      appBar: AppBar(
        leading: Navigator.canPop(context)
            ? IconButton(
                tooltip: 'Back',
                icon: const Icon(LucideIcons.arrowLeft),
                onPressed: () => Navigator.maybePop(context),
              )
            : null,
      ),
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
                  icon: LucideIcons.mapPin,
                  message: 'No rooms have been added yet, so there is nothing to report against.',
                )
              : _form(data),
        ),
      ),
    );
  }

  Widget _form(List<Room> rooms) {
    final theme = Theme.of(context);
    Room? room;
    for (final candidate in rooms) {
      if (candidate.id == _roomId) room = candidate;
    }

    return Column(
      children: [
        Expanded(
          child: ListView(
            keyboardDismissBehavior: ScrollViewKeyboardDismissBehavior.onDrag,
            padding: const EdgeInsets.fromLTRB(20, 4, 20, 24),
            children: [
              Text('Report a fault', style: theme.textTheme.headlineMedium),
              const SizedBox(height: 6),
              Text(
                "Say what's wrong and where. Facilities may ask you a question or two "
                'before sending someone.',
                style: theme.textTheme.bodyMedium?.copyWith(color: MxColors.graphite),
              ),
              const SizedBox(height: 22),

              if (_submitError != null) ...[
                _InlineAlert(message: _submitError!),
                const SizedBox(height: 14),
              ],

              // Optional, and first: scanning the sticker fills in the room and tells the
              // agents which machine this is, so they can read its repair history.
              if (_asset == null)
                _ScanRow(onTap: _isEditing ? _scanAsset : null)
              else
                _ScannedAssetCard(
                  asset: _asset!,
                  onRemove: _isEditing ? () => setState(() => _asset = null) : null,
                ),
              const SizedBox(height: 14),

              _WhatCard(
                controller: _descriptionController,
                errorText: _errors['description'],
                enabled: _isEditing,
                // The message goes once they start fixing it; validate() runs again on
                // submit, so nothing is let through by this.
                onChanged: (_) {
                  if (_errors.containsKey('description')) {
                    setState(() => _errors = {..._errors}..remove('description'));
                  }
                },
              ),
              const SizedBox(height: 6),
              _WhereCard(
                room: room,
                errorText: _errors['room'],
                onTap: _isEditing ? () => _chooseRoom(rooms) : null,
              ),
              const SizedBox(height: 22),

              if (_photo == null)
                _AddPhotoTile(
                  // A photo can be changed after an upload failure too — a 400 for the file
                  // itself is fixed by choosing a different one, not by retrying this one.
                  onTap: _isEditing || _phase == _Phase.uploadFailed ? _pickPhoto : null,
                )
              else
                _PhotoPreview(
                  photo: _photo!,
                  onChange:
                      _isEditing || _phase == _Phase.uploadFailed ? _pickPhoto : null,
                  // Removing is for before the report is filed. After an upload failure,
                  // "Continue without photo" is the way to drop it.
                  onRemove: _isEditing ? () => setState(() => _photo = null) : null,
                ),
              if (_photoError != null) ...[
                const SizedBox(height: 10),
                _InlineAlert(message: _photoError!),
              ],

            ],
          ),
        ),
        _ActionBar(children: _actions(theme)),
      ],
    );
  }

  List<Widget> _actions(ThemeData theme) {
    switch (_phase) {
      case _Phase.editing:
        return [FilledButton(onPressed: _submit, child: const Text('Submit report'))];

      case _Phase.filing:
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
                Text('Submitting…'),
              ],
            ),
          ),
        ];

      case _Phase.uploading:
        final percent = (_uploadProgress * 100).round();
        return [
          MxWell(
            padding: const EdgeInsets.fromLTRB(16, 14, 16, 16),
            radius: MxRadii.lg,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Row(
                  children: [
                    const Icon(LucideIcons.cloudUpload, size: 18),
                    const SizedBox(width: 10),
                    Expanded(
                      child: Text(
                        _uploadProgress < 1 ? 'Uploading photo… $percent%' : 'Saving photo…',
                        style: theme.textTheme.titleSmall?.copyWith(
                          fontFeatures: MxType.tabular,
                        ),
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 12),
                // Determinate while bytes are going out; indeterminate once they have all
                // gone and the API is putting the file into storage before it answers.
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
          // The report IS filed — said first, so nobody files it a second time.
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
                    'Your report has been filed, but the photo was not attached. '
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
                  onPressed: () => _finish('Report submitted without a photo.'),
                  child: const Text('Continue without photo'),
                ),
              ),
              const SizedBox(width: 10),
              Expanded(
                child: FilledButton(
                  onPressed: _uploadPhoto,
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

// ── The two joined cards ─────────────────────────────────────────────────────────────────

/// "What's wrong?" — the reporter's own words, as typed. Borderless inside a grey panel; the
/// panel's edge turns red when the description is refused.
class _WhatCard extends StatelessWidget {
  const _WhatCard({
    required this.controller,
    required this.errorText,
    required this.enabled,
    required this.onChanged,
  });

  final TextEditingController controller;
  final String? errorText;
  final bool enabled;
  final ValueChanged<String> onChanged;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return _FormPanel(
      hasError: errorText != null,
      padding: const EdgeInsets.fromLTRB(18, 16, 18, 10),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text("What's wrong?", style: theme.textTheme.labelMedium?.copyWith(color: MxColors.graphite)),
          TextField(
            controller: controller,
            enabled: enabled,
            onChanged: onChanged,
            minLines: 3,
            maxLines: 6,
            textCapitalization: TextCapitalization.sentences,
            style: theme.textTheme.bodyLarge?.copyWith(fontSize: 17, height: 1.4),
            decoration: const InputDecoration(
              hintText: 'e.g. the projector in this room will not power on',
              filled: false,
              isCollapsed: true,
              contentPadding: EdgeInsets.only(top: 10, bottom: 8),
              border: InputBorder.none,
              enabledBorder: InputBorder.none,
              focusedBorder: InputBorder.none,
              disabledBorder: InputBorder.none,
            ),
          ),
          if (errorText != null) _FieldError(errorText!),
        ],
      ),
    );
  }
}

/// "Where" — the room, chosen from a sheet. The connector on its top edge joins it to the
/// description above: one report, read top to bottom.
class _WhereCard extends StatelessWidget {
  const _WhereCard({required this.room, required this.errorText, required this.onTap});

  final Room? room;
  final String? errorText;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final chosen = room;

    return Stack(
      clipBehavior: Clip.none,
      children: [
        _FormPanel(
          hasError: errorText != null,
          onTap: onTap,
          padding: const EdgeInsets.fromLTRB(18, 18, 14, 16),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Semantics(
                button: true,
                label: chosen == null ? 'Choose a room' : 'Room ${chosen.label}. Change room',
                excludeSemantics: true,
                child: Row(
                  children: [
                    const MxIconTile(
                      icon: LucideIcons.mapPin,
                      background: MxColors.surface,
                      size: 44,
                    ),
                    const SizedBox(width: 14),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            'Where',
                            style: theme.textTheme.labelMedium?.copyWith(color: MxColors.graphite),
                          ),
                          const SizedBox(height: 3),
                          if (chosen == null)
                            Text(
                              'Choose a room',
                              style: theme.textTheme.titleMedium?.copyWith(color: MxColors.mute),
                            )
                          else ...[
                            Text(
                              chosen.name,
                              style: theme.textTheme.titleMedium,
                            ),
                            Text(
                              '${chosen.code}, ${_floorLabel(chosen.floor)}',
                              style: theme.textTheme.bodySmall,
                            ),
                          ],
                        ],
                      ),
                    ),
                    Icon(
                      LucideIcons.chevronDown,
                      size: 20,
                      color: onTap == null ? MxColors.mute : MxColors.ink,
                    ),
                  ],
                ),
              ),
              if (errorText != null) _FieldError(errorText!),
            ],
          ),
        ),
        Positioned(
          top: -19,
          left: 0,
          right: 0,
          child: Center(
            child: ExcludeSemantics(
              child: Container(
                width: 32,
                height: 32,
                decoration: BoxDecoration(
                  color: MxColors.surface,
                  shape: BoxShape.circle,
                  border: Border.all(color: MxColors.surface, width: 3),
                ),
                child: Container(
                  decoration: const BoxDecoration(color: MxColors.well, shape: BoxShape.circle),
                  child: const Icon(LucideIcons.arrowDown, size: 14, color: MxColors.graphite),
                ),
              ),
            ),
          ),
        ),
      ],
    );
  }
}

String _floorLabel(int floor) => floor == 0 ? 'ground floor' : 'floor $floor';

class _FormPanel extends StatelessWidget {
  const _FormPanel({
    required this.child,
    required this.hasError,
    required this.padding,
    this.onTap,
  });

  final Widget child;
  final bool hasError;
  final EdgeInsetsGeometry padding;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    return Material(
      color: MxColors.well,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(MxRadii.lg),
        side: BorderSide(color: hasError ? MxColors.redLine : Colors.transparent, width: 1.2),
      ),
      clipBehavior: Clip.antiAlias,
      child: InkWell(onTap: onTap, child: Padding(padding: padding, child: child)),
    );
  }
}

class _FieldError extends StatelessWidget {
  const _FieldError(this.message);

  final String message;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(top: 8),
      child: Text(
        message,
        style: Theme.of(context).textTheme.bodySmall?.copyWith(color: MxColors.red),
      ),
    );
  }
}

class _InlineAlert extends StatelessWidget {
  const _InlineAlert({required this.message});

  final String message;

  @override
  Widget build(BuildContext context) {
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const Padding(
          padding: EdgeInsets.only(top: 2),
          child: Icon(LucideIcons.circleAlert, size: 16, color: MxColors.red),
        ),
        const SizedBox(width: 8),
        Expanded(
          child: Text(
            message,
            style: Theme.of(context).textTheme.bodyMedium?.copyWith(color: MxColors.red),
          ),
        ),
      ],
    );
  }
}

// ── Room picker ──────────────────────────────────────────────────────────────────────────

/// Every room from GET /api/rooms, with a box that narrows the list as you type. The
/// narrowing is a find-in-list over rooms already fetched — no request, no rule.
class _RoomSheet extends StatefulWidget {
  const _RoomSheet({required this.rooms, required this.selectedId});

  final List<Room> rooms;
  final int? selectedId;

  @override
  State<_RoomSheet> createState() => _RoomSheetState();
}

class _RoomSheetState extends State<_RoomSheet> {
  String _filter = '';

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final needle = _filter.trim().toLowerCase();
    final shown = needle.isEmpty
        ? widget.rooms
        : widget.rooms
            .where((room) =>
                room.name.toLowerCase().contains(needle) ||
                room.code.toLowerCase().contains(needle))
            .toList(growable: false);

    return SafeArea(
      child: SizedBox(
        height: MediaQuery.sizeOf(context).height * 0.78,
        child: Padding(
          padding: EdgeInsets.only(bottom: MediaQuery.viewInsetsOf(context).bottom),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Padding(
                padding: const EdgeInsets.fromLTRB(20, 0, 20, 14),
                child: Text('Choose a room', style: theme.textTheme.headlineSmall),
              ),
              Padding(
                padding: const EdgeInsets.symmetric(horizontal: 20),
                child: TextField(
                  onChanged: (value) => setState(() => _filter = value),
                  textInputAction: TextInputAction.search,
                  decoration: const InputDecoration(
                    hintText: 'Search by room name or code',
                    prefixIcon: Icon(LucideIcons.search, size: 18),
                    contentPadding: EdgeInsets.symmetric(vertical: 14),
                    border: OutlineInputBorder(
                      borderRadius: BorderRadius.all(Radius.circular(999)),
                      borderSide: BorderSide.none,
                    ),
                    enabledBorder: OutlineInputBorder(
                      borderRadius: BorderRadius.all(Radius.circular(999)),
                      borderSide: BorderSide.none,
                    ),
                    focusedBorder: OutlineInputBorder(
                      borderRadius: BorderRadius.all(Radius.circular(999)),
                      borderSide: BorderSide(color: MxColors.iris, width: 1.6),
                    ),
                  ),
                ),
              ),
              const SizedBox(height: 8),
              Expanded(
                child: shown.isEmpty
                    ? Center(
                        child: Text(
                          'No room matches "${_filter.trim()}".',
                          style: theme.textTheme.bodyMedium?.copyWith(color: MxColors.graphite),
                        ),
                      )
                    : ListView.builder(
                        padding: const EdgeInsets.fromLTRB(12, 4, 12, 16),
                        itemCount: shown.length,
                        itemBuilder: (context, index) {
                          final room = shown[index];
                          final selected = room.id == widget.selectedId;
                          return _RoomRow(
                            room: room,
                            selected: selected,
                            onTap: () => Navigator.pop(context, room.id),
                          );
                        },
                      ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _RoomRow extends StatelessWidget {
  const _RoomRow({required this.room, required this.selected, required this.onTap});

  final Room room;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Material(
      color: selected ? MxColors.well : Colors.transparent,
      borderRadius: BorderRadius.circular(MxRadii.md),
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(MxRadii.md),
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 12),
          child: Row(
            children: [
              Container(
                constraints: const BoxConstraints(minWidth: 76),
                padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 7),
                decoration: BoxDecoration(
                  color: selected ? MxColors.ink : MxColors.well,
                  borderRadius: BorderRadius.circular(10),
                ),
                child: Text(
                  room.code,
                  textAlign: TextAlign.center,
                  style: theme.textTheme.labelMedium?.copyWith(
                    color: selected ? Colors.white : MxColors.ink,
                    fontWeight: FontWeight.w600,
                    fontFeatures: MxType.tabular,
                  ),
                ),
              ),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(room.name, style: theme.textTheme.titleSmall),
                    Text(
                      _floorLabel(room.floor).replaceFirstMapped(
                        RegExp('^.'),
                        (m) => m[0]!.toUpperCase(),
                      ),
                      style: theme.textTheme.bodySmall,
                    ),
                  ],
                ),
              ),
              if (selected) const Icon(LucideIcons.check, size: 18),
            ],
          ),
        ),
      ),
    );
  }
}

// ── Photo ────────────────────────────────────────────────────────────────────────────────

class _AddPhotoTile extends StatelessWidget {
  const _AddPhotoTile({required this.onTap});

  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return CustomPaint(
      painter: const _DashedBorderPainter(radius: MxRadii.lg),
      child: Material(
        color: Colors.transparent,
        borderRadius: BorderRadius.circular(MxRadii.lg),
        child: InkWell(
          onTap: onTap,
          borderRadius: BorderRadius.circular(MxRadii.lg),
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 18, vertical: 18),
            child: Row(
              children: [
                const MxIconTile(icon: LucideIcons.camera),
                const SizedBox(width: 14),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text('Add photo', style: theme.textTheme.titleMedium),
                      const SizedBox(height: 2),
                      Text('Optional. JPEG or PNG, up to 5 MB.', style: theme.textTheme.bodySmall),
                    ],
                  ),
                ),
                const Icon(LucideIcons.plus, size: 20),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _PhotoPreview extends StatelessWidget {
  const _PhotoPreview({required this.photo, required this.onChange, required this.onRemove});

  final PickedPhoto photo;
  final VoidCallback? onChange;
  final VoidCallback? onRemove;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return ClipRRect(
      borderRadius: BorderRadius.circular(MxRadii.lg),
      child: SizedBox(
        height: 200,
        child: Stack(
          fit: StackFit.expand,
          children: [
            Image.memory(
              photo.bytes,
              fit: BoxFit.cover,
              // A photo the phone cannot preview still uploads — the API is what judges it.
              errorBuilder: (context, _, __) => const ColoredBox(
                color: MxColors.well,
                child: Icon(LucideIcons.image, size: 32, color: MxColors.mute),
              ),
            ),
            const DecoratedBox(
              decoration: BoxDecoration(
                gradient: LinearGradient(
                  begin: Alignment.topCenter,
                  end: Alignment.bottomCenter,
                  stops: [0.55, 1],
                  colors: [Colors.transparent, Color(0x9915171C)],
                ),
              ),
            ),
            if (onRemove != null)
              Positioned(
                top: 10,
                right: 10,
                // Dark, so it shows over a white photo as well as a dark one.
                child: MxRoundButton(
                  tooltip: 'Remove photo',
                  icon: LucideIcons.x,
                  background: MxColors.ink.withValues(alpha: 0.72),
                  foreground: Colors.white,
                  onPressed: onRemove,
                ),
              ),
            Positioned(
              left: 14,
              right: 10,
              bottom: 10,
              child: Row(
                children: [
                  Container(
                    padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
                    decoration: BoxDecoration(
                      color: MxColors.ink.withValues(alpha: 0.72),
                      borderRadius: BorderRadius.circular(999),
                    ),
                    child: Text(
                      '${photo.contentType == 'image/png' ? 'PNG' : 'JPEG'}, ${photo.sizeLabel}',
                      style: theme.textTheme.labelMedium?.copyWith(
                        color: Colors.white,
                        fontFeatures: MxType.tabular,
                      ),
                    ),
                  ),
                  const Spacer(),
                  if (onChange != null)
                    FilledButton.icon(
                      onPressed: onChange,
                      style: FilledButton.styleFrom(
                        backgroundColor: Colors.white,
                        foregroundColor: MxColors.ink,
                        minimumSize: const Size(0, 38),
                        padding: const EdgeInsets.symmetric(horizontal: 14),
                      ),
                      icon: const Icon(LucideIcons.camera, size: 16),
                      label: const Text('Change photo'),
                    ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// "Scan the equipment's sticker" — the scanner's way in. Optional; the form is complete
/// without it.
class _ScanRow extends StatelessWidget {
  const _ScanRow({required this.onTap});

  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Material(
      color: MxColors.well,
      borderRadius: BorderRadius.circular(MxRadii.lg),
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(MxRadii.lg),
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 18, vertical: 14),
          child: Row(
            children: [
              const MxIconTile(icon: LucideIcons.scanQrCode, background: MxColors.surface),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text("Scan the equipment's sticker", style: theme.textTheme.titleSmall),
                    const SizedBox(height: 2),
                    Text(
                      'Optional. Fills in the room and tells Facilities which machine it is.',
                      style: theme.textTheme.bodySmall,
                    ),
                  ],
                ),
              ),
              Icon(
                LucideIcons.chevronRight,
                size: 20,
                color: onTap == null ? MxColors.mute : MxColors.ink,
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// The machine a scanned sticker named, with a way to take it off the report.
class _ScannedAssetCard extends StatelessWidget {
  const _ScannedAssetCard({required this.asset, required this.onRemove});

  final AssetDetail asset;
  final VoidCallback? onRemove;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return MxWell(
      radius: MxRadii.lg,
      padding: const EdgeInsets.fromLTRB(18, 12, 8, 12),
      child: Row(
        children: [
          const MxIconTile(icon: LucideIcons.scanQrCode, background: MxColors.surface),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text('Equipment', style: theme.textTheme.labelMedium?.copyWith(color: MxColors.graphite)),
                const SizedBox(height: 2),
                Text(asset.name, style: theme.textTheme.titleSmall),
                Text(
                  asset.assetTag,
                  style: theme.textTheme.bodySmall?.copyWith(fontFeatures: MxType.tabular),
                ),
              ],
            ),
          ),
          if (onRemove != null)
            IconButton(
              tooltip: 'Remove equipment',
              icon: const Icon(LucideIcons.x, size: 18),
              onPressed: onRemove,
            ),
        ],
      ),
    );
  }
}

/// The bar pinned under the form: the one action, or the upload's progress, or what to do
/// after an upload failed.
class _ActionBar extends StatelessWidget {
  const _ActionBar({required this.children});

  final List<Widget> children;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.fromLTRB(20, 12, 20, 16),
      decoration: const BoxDecoration(
        color: MxColors.surface,
        border: Border(top: BorderSide(color: MxColors.hairlineSoft)),
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: children,
      ),
    );
  }
}

class _DashedBorderPainter extends CustomPainter {
  const _DashedBorderPainter({required this.radius});

  final double radius;

  @override
  void paint(Canvas canvas, Size size) {
    final paint = Paint()
      ..color = const Color(0xFFC9CDD4)
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1.4;
    final path = Path()
      ..addRRect(RRect.fromRectAndRadius(
        Offset.zero & size,
        Radius.circular(radius),
      ).deflate(0.7));
    const dash = 6.0;
    const gap = 5.0;
    for (final metric in path.computeMetrics()) {
      var distance = 0.0;
      while (distance < metric.length) {
        canvas.drawPath(metric.extractPath(distance, distance + dash), paint);
        distance += dash + gap;
      }
    }
  }

  @override
  bool shouldRepaint(covariant _DashedBorderPainter oldDelegate) => oldDelegate.radius != radius;
}
