import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../../core/api_client.dart';
import '../../core/app_theme.dart';
import '../../widgets/empty_view.dart';
import '../../widgets/error_view.dart';
import '../../widgets/surfaces.dart';
import 'clarification.dart';
import 'my_reports_screen.dart';
import 'reports_api.dart';

/// The few seconds between filing a report and the clarifier's answer.
///
/// Filing returns as soon as the report is stored; the clarifier runs afterwards in the
/// API's background runner, so its questions do not exist yet when the report does. This
/// polls `GET /api/reports/{id}` until they do and hands them to [onQuestions] — the form
/// then opens in place, without the reporter going to the list and pulling to refresh.
///
/// Nothing here is a conversation: it waits for ONE set of questions and stops.
class ClarifierWait extends ConsumerStatefulWidget {
  const ClarifierWait({super.key, required this.reportId, required this.onQuestions});

  final int reportId;
  final ValueChanged<List<ClarificationQuestion>> onQuestions;

  static const Duration pollEvery = Duration(seconds: 2);

  /// When "checking…" becomes "taking longer than usual". A report with nothing to ask runs
  /// the diagnostic and strategist in the same agent call before the clarifier's step is
  /// recorded, so that answer arrives later than a question does.
  static const Duration slowAfter = Duration(seconds: 30);

  /// When the screen stops asking. Any questions after that wait in My reports.
  static const Duration giveUpAfter = Duration(minutes: 3);

  /// Consecutive failed polls before the wait becomes an error. One dropped request on a
  /// phone is not worth interrupting anybody for.
  static const int maxConsecutiveErrors = 3;

  @override
  ConsumerState<ClarifierWait> createState() => _ClarifierWaitState();
}

enum _Phase { waiting, nothingToAsk, failed, gaveUp, error }

class _ClarifierWaitState extends ConsumerState<ClarifierWait> {
  Timer? _next;
  Timer? _slowTimer;
  Timer? _giveUpTimer;

  _Phase _phase = _Phase.waiting;
  bool _slow = false;
  int _errors = 0;
  String? _errorMessage;

  /// A 403 or 404 will not change by asking again.
  bool _canRetry = true;

  @override
  void initState() {
    super.initState();
    _start();
  }

  @override
  void dispose() {
    _cancelTimers();
    super.dispose();
  }

  void _cancelTimers() {
    _next?.cancel();
    _slowTimer?.cancel();
    _giveUpTimer?.cancel();
  }

  void _start() {
    _errors = 0;
    _slowTimer = Timer(ClarifierWait.slowAfter, () {
      if (mounted) setState(() => _slow = true);
    });
    _giveUpTimer = Timer(ClarifierWait.giveUpAfter, () {
      if (mounted) _stop(_Phase.gaveUp);
    });
    _poll();
  }

  void _restart() {
    setState(() {
      _phase = _Phase.waiting;
      _slow = false;
      _errorMessage = null;
    });
    _start();
  }

  void _stop(_Phase phase) {
    _cancelTimers();
    setState(() => _phase = phase);
  }

  /// One request, then the next is scheduled only once it has answered, so a slow network
  /// never stacks requests on top of each other.
  Future<void> _poll() async {
    try {
      final result = await ref.read(reportsApiProvider).clarifierProgress(widget.reportId);
      // Gone, or the time limit ended the wait while this request was out.
      if (!mounted || _phase != _Phase.waiting) return;
      _errors = 0;

      switch (result.progress) {
        case ClarifierProgress.asked:
          _cancelTimers();
          // The report row now says "waiting on you"; the list must not show it stale.
          ref.invalidate(reportsPageProvider);
          widget.onQuestions(result.questions);
          return;
        case ClarifierProgress.nothingToAsk:
          _stop(_Phase.nothingToAsk);
          return;
        case ClarifierProgress.failed:
          _stop(_Phase.failed);
          return;
        case ClarifierProgress.running:
          break;
      }
    } on ApiException catch (error) {
      if (!mounted || _phase != _Phase.waiting) return;
      final permanent = error.statusCode == 403 || error.statusCode == 404;
      _errors++;
      if (permanent || _errors >= ClarifierWait.maxConsecutiveErrors) {
        _canRetry = !permanent;
        _errorMessage = error.message;
        _stop(_Phase.error);
        return;
      }
    }

    _next = Timer(ClarifierWait.pollEvery, _poll);
  }

  void _toMyReports() => context.go(MyReportsScreen.path);

  @override
  Widget build(BuildContext context) {
    final toList = FilledButton(onPressed: _toMyReports, child: const Text('Go to my reports'));

    switch (_phase) {
      case _Phase.nothingToAsk:
        return EmptyView(
          icon: LucideIcons.circleCheck,
          message: 'Report filed. No questions needed — Facilities have what they need. '
              'You can follow it in My reports.',
          action: toList,
        );
      case _Phase.failed:
        return EmptyView(
          icon: LucideIcons.circleCheck,
          message: 'Report filed. It could not be checked automatically, so Facilities '
              'will review it as you wrote it. Nothing more is needed from you.',
          action: toList,
        );
      case _Phase.gaveUp:
        return EmptyView(
          icon: LucideIcons.clock,
          message: 'Report filed. The check is taking longer than usual. If Facilities '
              'need anything, the questions will be waiting in My reports.',
          action: toList,
        );
      case _Phase.error:
        return ErrorView(
          title: 'Could not check your report',
          // Said first, so nobody files it a second time.
          message: 'Your report has been filed. ${_errorMessage ?? ''}',
          onRetry: _canRetry ? _restart : null,
        );
      case _Phase.waiting:
        return _waiting(context);
    }
  }

  Widget _waiting(BuildContext context) {
    final theme = Theme.of(context);

    return Column(
      children: [
        Expanded(
          child: ListView(
            padding: const EdgeInsets.fromLTRB(20, 4, 20, 24),
            children: [
              const Align(
                alignment: Alignment.centerLeft,
                child: MxIconTile(icon: LucideIcons.circleCheck),
              ),
              const SizedBox(height: 16),
              Text('Report filed', style: theme.textTheme.headlineMedium),
              const SizedBox(height: 6),
              Text(
                'Facilities may need a detail or two before they send someone. If they do, '
                'the questions will open here — usually within a few seconds.',
                style: theme.textTheme.bodyMedium?.copyWith(color: MxColors.graphite),
              ),
              const SizedBox(height: 22),
              Semantics(
                liveRegion: true,
                child: MxWell(
                  radius: MxRadii.lg,
                  padding: const EdgeInsets.fromLTRB(16, 16, 16, 16),
                  child: Row(
                    children: [
                      const SizedBox(
                        width: 18,
                        height: 18,
                        child: CircularProgressIndicator(
                          strokeWidth: 2,
                          strokeCap: StrokeCap.round,
                        ),
                      ),
                      const SizedBox(width: 14),
                      Expanded(
                        child: Text(
                          _slow
                              ? 'Still checking — this is taking longer than usual.'
                              : 'Checking your report…',
                          style: theme.textTheme.titleSmall,
                        ),
                      ),
                    ],
                  ),
                ),
              ),
            ],
          ),
        ),
        MxActionBar(
          children: [
            Text(
              'You can leave — any questions will wait for you in My reports.',
              textAlign: TextAlign.center,
              style: theme.textTheme.bodySmall?.copyWith(color: MxColors.graphite),
            ),
            const SizedBox(height: 10),
            OutlinedButton(onPressed: _toMyReports, child: const Text('Go to my reports')),
          ],
        ),
      ],
    );
  }
}
