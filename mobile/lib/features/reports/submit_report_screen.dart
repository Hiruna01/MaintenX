import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api_client.dart';
import '../../widgets/app_form_field.dart';
import '../../widgets/empty_view.dart';
import '../../widgets/error_view.dart';
import '../../widgets/loading_view.dart';
import '../home/home_screen.dart';
import 'reports_api.dart';
import 'room.dart';

/// Submit a maintenance report: what is wrong, and where.
///
/// There is no chat interface here and there will not be one. When the agent needs more
/// detail, its clarification questions will be rendered as another FORM with bounded
/// inputs — pickers, yes/no, short text — never as a message thread.
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
  bool _isSubmitting = false;

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

  Future<void> _submit() async {
    final errors = _validate();
    setState(() {
      _errors = errors;
      _submitError = null;
    });

    if (errors.isNotEmpty) return;

    setState(() => _isSubmitting = true);

    try {
      await ref.read(reportsApiProvider).submit(
            description: _descriptionController.text.trim(),
            roomId: _roomId!,
          );

      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Report submitted.')),
      );
      context.go(HomeScreen.path);
    } on ApiException catch (error) {
      if (mounted) setState(() => _submitError = error.message);
    } finally {
      if (mounted) setState(() => _isSubmitting = false);
    }
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
    return ListView(
      padding: const EdgeInsets.all(24),
      children: [
        if (_submitError != null) ...[
          Text(
            _submitError!,
            style: TextStyle(color: Theme.of(context).colorScheme.error),
          ),
          const SizedBox(height: 16),
        ],

        AppFormField(
          label: 'What is wrong?',
          controller: _descriptionController,
          errorText: _errors['description'],
          hintText: 'e.g. the projector in this room will not power on',
          maxLines: 4,
          enabled: !_isSubmitting,
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
            if (_isSubmitting) return;
            setState(() {
              _roomId = value;
              _errors = {..._errors}..remove('room');
            });
          },
        ),

        // Hooks for the next two features. Disabled rather than hidden so the shape of the
        // finished form is visible, and so wiring them up is a change in one place.
        const SizedBox(height: 8),
        Row(
          children: [
            Expanded(
              child: OutlinedButton.icon(
                // TODO(photo): image_picker -> multipart upload, then send the returned id.
                onPressed: null,
                icon: const Icon(Icons.photo_camera_outlined),
                label: const Text('Add photo'),
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
          'Photo and QR scanning are coming soon.',
          style: Theme.of(context).textTheme.bodySmall,
        ),

        const SizedBox(height: 24),
        FilledButton(
          onPressed: _isSubmitting ? null : _submit,
          child: Text(_isSubmitting ? 'Submitting…' : 'Submit report'),
        ),
      ],
    );
  }
}
