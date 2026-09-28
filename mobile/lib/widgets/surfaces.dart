import 'package:flutter/material.dart';
import 'package:lucide_icons_flutter/lucide_icons.dart';

import '../core/app_theme.dart';

/// A white card on the grey canvas. Flat — the canvas is what separates it, not a shadow.
class MxCard extends StatelessWidget {
  const MxCard({
    super.key,
    required this.child,
    this.onTap,
    this.padding = const EdgeInsets.all(18),
    this.color = MxColors.surface,
    this.radius = MxRadii.lg,
  });

  final Widget child;
  final VoidCallback? onTap;
  final EdgeInsetsGeometry padding;
  final Color color;
  final double radius;

  @override
  Widget build(BuildContext context) {
    final shape = RoundedRectangleBorder(borderRadius: BorderRadius.circular(radius));
    return Material(
      color: color,
      shape: shape,
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: onTap,
        child: Padding(padding: padding, child: child),
      ),
    );
  }
}

/// A grey panel inside a white screen or card — the "Pay with" block in the references.
class MxWell extends StatelessWidget {
  const MxWell({
    super.key,
    required this.child,
    this.padding = const EdgeInsets.all(16),
    this.radius = MxRadii.md,
    this.color = MxColors.well,
  });

  final Widget child;
  final EdgeInsetsGeometry padding;
  final double radius;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: padding,
      decoration: BoxDecoration(color: color, borderRadius: BorderRadius.circular(radius)),
      child: child,
    );
  }
}

/// An icon on a small rounded tile, leading a row.
class MxIconTile extends StatelessWidget {
  const MxIconTile({
    super.key,
    required this.icon,
    this.size = 44,
    this.background = MxColors.well,
    this.foreground = MxColors.ink,
  });

  final IconData icon;
  final double size;
  final Color background;
  final Color foreground;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: size,
      height: size,
      decoration: BoxDecoration(
        color: background,
        borderRadius: BorderRadius.circular(size * 0.32),
      ),
      child: Icon(icon, size: size * 0.45, color: foreground),
    );
  }
}

/// A round white icon button — back, add, sign out. Keeps its tooltip, which is also its
/// accessible name.
class MxRoundButton extends StatelessWidget {
  const MxRoundButton({
    super.key,
    required this.icon,
    required this.tooltip,
    required this.onPressed,
    this.background = MxColors.surface,
    this.foreground = MxColors.ink,
  });

  final IconData icon;
  final String tooltip;
  final VoidCallback? onPressed;
  final Color background;
  final Color foreground;

  @override
  Widget build(BuildContext context) {
    return IconButton(
      tooltip: tooltip,
      onPressed: onPressed,
      style: IconButton.styleFrom(
        backgroundColor: background,
        foregroundColor: foreground,
        fixedSize: const Size(44, 44),
        shape: const CircleBorder(),
      ),
      icon: Icon(icon, size: 20),
    );
  }
}

/// The MaintenX mark: a QR code's finder square. The sticker on every machine is where a
/// report starts, so the product signs itself with the corner of one.
class MxBrandMark extends StatelessWidget {
  const MxBrandMark({super.key, this.size = 30, this.color = MxColors.ink});

  final double size;
  final Color color;

  @override
  Widget build(BuildContext context) {
    final ring = size * 0.14;
    return Container(
      width: size,
      height: size,
      padding: EdgeInsets.all(ring),
      decoration: BoxDecoration(color: color, borderRadius: BorderRadius.circular(size * 0.3)),
      child: Container(
        padding: EdgeInsets.all(ring),
        decoration: BoxDecoration(
          color: Colors.white,
          borderRadius: BorderRadius.circular(size * 0.18),
        ),
        child: DecoratedBox(
          decoration: BoxDecoration(color: color, borderRadius: BorderRadius.circular(size * 0.1)),
        ),
      ),
    );
  }
}

/// The mark and the word, for the top of a signed-out or home screen.
class MxWordmark extends StatelessWidget {
  const MxWordmark({super.key});

  @override
  Widget build(BuildContext context) {
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        const MxBrandMark(size: 28),
        const SizedBox(width: 10),
        Text(
          'MaintenX',
          style: Theme.of(context).textTheme.titleLarge?.copyWith(letterSpacing: -0.5),
        ),
      ],
    );
  }
}

/// A grey block standing in for content that is still loading, pulsing gently. Shaped like
/// what it replaces, so the page does not jump when the data lands.
class Skeleton extends StatefulWidget {
  const Skeleton({super.key, this.width, required this.height, this.radius = 8});

  final double? width;
  final double height;
  final double radius;

  @override
  State<Skeleton> createState() => _SkeletonState();
}

class _SkeletonState extends State<Skeleton> with SingleTickerProviderStateMixin {
  late final AnimationController _controller = AnimationController(
    vsync: this,
    duration: const Duration(milliseconds: 1100),
  );

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    // Reduced motion: hold still at the midpoint rather than pulse.
    if (MediaQuery.maybeDisableAnimationsOf(context) ?? false) {
      _controller
        ..stop()
        ..value = 0.5;
    } else if (!_controller.isAnimating) {
      _controller.repeat(reverse: true);
    }
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return FadeTransition(
      opacity: Tween<double>(begin: 0.55, end: 1).animate(_controller),
      child: Container(
        width: widget.width,
        height: widget.height,
        decoration: BoxDecoration(
          color: MxColors.wellDeep,
          borderRadius: BorderRadius.circular(widget.radius),
        ),
      ),
    );
  }
}

/// Joins [child] to the panel above it with a small round arrow sitting on the seam — one
/// account read top to bottom (what's wrong → where; reported → repaired). [ring] should be
/// the colour behind the panels, so the connector cuts cleanly into both.
class MxJoined extends StatelessWidget {
  const MxJoined({super.key, required this.child, this.ring = MxColors.surface});

  final Widget child;
  final Color ring;

  @override
  Widget build(BuildContext context) {
    return Stack(
      clipBehavior: Clip.none,
      children: [
        child,
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
                  color: ring,
                  shape: BoxShape.circle,
                  border: Border.all(color: ring, width: 3),
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

/// The bar pinned under a form: its one action, and whatever state that action is in.
class MxActionBar extends StatelessWidget {
  const MxActionBar({super.key, required this.children, this.color = MxColors.surface});

  final List<Widget> children;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.fromLTRB(20, 12, 20, 16),
      decoration: BoxDecoration(
        color: color,
        border: const Border(top: BorderSide(color: MxColors.hairlineSoft)),
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: children,
      ),
    );
  }
}

/// Previous / "Page n of m" / next, under a paged list. The page numbers are the API's.
class MxPager extends StatelessWidget {
  const MxPager({
    super.key,
    required this.page,
    required this.totalPages,
    required this.caption,
    required this.onPrevious,
    required this.onNext,
  });

  final int page;
  final int totalPages;

  /// What is being paged, counted by the API — "24 reports".
  final String caption;
  final VoidCallback? onPrevious;
  final VoidCallback? onNext;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Row(
      children: [
        MxRoundButton(
          tooltip: 'Previous page',
          icon: LucideIcons.chevronLeft,
          onPressed: onPrevious,
        ),
        Expanded(
          child: Column(
            children: [
              Text(
                'Page $page of $totalPages',
                textAlign: TextAlign.center,
                style: theme.textTheme.titleSmall?.copyWith(fontFeatures: MxType.tabular),
              ),
              Text(
                caption,
                textAlign: TextAlign.center,
                style: theme.textTheme.bodySmall?.copyWith(fontFeatures: MxType.tabular),
              ),
            ],
          ),
        ),
        MxRoundButton(
          tooltip: 'Next page',
          icon: LucideIcons.chevronRight,
          onPressed: onNext,
        ),
      ],
    );
  }
}

/// A short grey label naming the panel it sits in — "What you reported".
class MxPanelLabel extends StatelessWidget {
  const MxPanelLabel(this.text, {super.key, this.icon});

  final String text;
  final IconData? icon;

  @override
  Widget build(BuildContext context) {
    final style = Theme.of(context).textTheme.labelMedium?.copyWith(color: MxColors.graphite);
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        if (icon != null) ...[
          Icon(icon, size: 14, color: MxColors.graphite),
          const SizedBox(width: 6),
        ],
        Text(text, style: style),
      ],
    );
  }
}

/// A dashed, tappable tile for something optional to add — a photo. Dashed because it is a
/// place for something that is not there yet.
class MxDashedTile extends StatelessWidget {
  const MxDashedTile({
    super.key,
    required this.icon,
    required this.title,
    required this.subtitle,
    required this.onTap,
  });

  final IconData icon;
  final String title;
  final String subtitle;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return CustomPaint(
      painter: const _DashedRRectPainter(radius: MxRadii.lg),
      child: Material(
        color: Colors.transparent,
        borderRadius: BorderRadius.circular(MxRadii.lg),
        child: InkWell(
          onTap: onTap,
          borderRadius: BorderRadius.circular(MxRadii.lg),
          child: Padding(
            padding: const EdgeInsets.all(18),
            child: Row(
              children: [
                MxIconTile(icon: icon),
                const SizedBox(width: 14),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(title, style: theme.textTheme.titleMedium),
                      const SizedBox(height: 2),
                      Text(subtitle, style: theme.textTheme.bodySmall),
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

class _DashedRRectPainter extends CustomPainter {
  const _DashedRRectPainter({required this.radius});

  final double radius;

  @override
  void paint(Canvas canvas, Size size) {
    final paint = Paint()
      ..color = const Color(0xFFC9CDD4)
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1.4;
    final path = Path()
      ..addRRect(RRect.fromRectAndRadius(Offset.zero & size, Radius.circular(radius)).deflate(0.7));
    for (final metric in path.computeMetrics()) {
      var distance = 0.0;
      while (distance < metric.length) {
        canvas.drawPath(metric.extractPath(distance, distance + 6), paint);
        distance += 11;
      }
    }
  }

  @override
  bool shouldRepaint(covariant _DashedRRectPainter oldDelegate) => oldDelegate.radius != radius;
}
