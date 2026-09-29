import 'package:flutter/material.dart';

import '../core/app_theme.dart';

/// The loading state. Every screen that waits on the API shows this — a blank screen while
/// loading is a bug. Lists that know their own shape show skeleton rows instead.
class LoadingView extends StatelessWidget {
  const LoadingView({super.key, this.message = 'Loading…'});

  final String message;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          const SizedBox(
            width: 26,
            height: 26,
            child: CircularProgressIndicator(strokeWidth: 2.4, strokeCap: StrokeCap.round),
          ),
          const SizedBox(height: 16),
          Text(
            message,
            style: Theme.of(context).textTheme.bodyMedium?.copyWith(color: MxColors.graphite),
          ),
        ],
      ),
    );
  }
}
