import 'package:flutter/material.dart';

/// The palette — the web client's `--mx-*` tokens (`web/src/components/ui/tokens.css`), so
/// the phone and the browser read as one product.
///
/// Black, white and greys carry the whole interface. [iris] is the one accent, and it means
/// exactly one thing: something is waiting on YOU — a clarification to answer, a repair to
/// confirm — plus the focus ring. Anything else in iris dilutes that signal.
class MxColors {
  const MxColors._();

  static const canvas = Color(0xFFEEF0F3);
  static const surface = Color(0xFFFFFFFF);

  /// Grey panels inside a white screen or card ("Pay with" in the references).
  static const well = Color(0xFFF4F5F7);
  static const wellDeep = Color(0xFFEBEDF0);

  static const ink = Color(0xFF15171C);
  static const ink2 = Color(0xFF2B2E36);
  static const graphite = Color(0xFF6A707C);
  static const mute = Color(0xFF9AA0AA);
  static const hairline = Color(0xFFE3E6EB);
  static const hairlineSoft = Color(0xFFECEEF2);

  static const iris = Color(0xFF5B50E8);
  static const irisSoft = Color(0xFFEFEEFD);

  static const red = Color(0xFFC2341D);
  static const redBg = Color(0xFFFDEEEB);
  static const redLine = Color(0xFFF6D0C9);
  static const amber = Color(0xFFB25E09);
  static const amberBg = Color(0xFFFDF4E6);
  static const amberLine = Color(0xFFF4DCB4);
}

class MxRadii {
  const MxRadii._();

  static const double sm = 10;
  static const double md = 14;
  static const double lg = 20;
  static const double xl = 26;
}

/// Hanken Grotesk everywhere; figures are tabular wherever numbers line up or tick over.
class MxType {
  const MxType._();

  static const String family = 'HankenGrotesk';
  static const List<FontFeature> tabular = [FontFeature.tabularFigures()];
}

/// The one elevation the design uses — a soft, wide shadow under the few things that float
/// (the Home hero card, a pinned action bar). Everything else sits flat on its background.
const List<BoxShadow> mxFloatShadow = [
  BoxShadow(color: Color(0x2915171C), blurRadius: 40, offset: Offset(0, 18), spreadRadius: -18),
  BoxShadow(color: Color(0x0A15171C), blurRadius: 6, offset: Offset(0, 2)),
];

class AppTheme {
  const AppTheme._();

  static ThemeData light() {
    const scheme = ColorScheme(
      brightness: Brightness.light,
      primary: MxColors.ink,
      onPrimary: Colors.white,
      primaryContainer: MxColors.irisSoft,
      onPrimaryContainer: MxColors.iris,
      // FilledButton.tonal reads these: a quiet grey pill beside the ink one.
      secondary: MxColors.iris,
      onSecondary: Colors.white,
      secondaryContainer: MxColors.wellDeep,
      onSecondaryContainer: MxColors.ink,
      tertiary: MxColors.iris,
      onTertiary: Colors.white,
      error: MxColors.red,
      onError: Colors.white,
      errorContainer: MxColors.redBg,
      onErrorContainer: MxColors.red,
      surface: MxColors.surface,
      onSurface: MxColors.ink,
      onSurfaceVariant: MxColors.graphite,
      surfaceContainerLowest: MxColors.surface,
      surfaceContainerLow: MxColors.well,
      surfaceContainer: MxColors.well,
      surfaceContainerHigh: MxColors.wellDeep,
      surfaceContainerHighest: MxColors.wellDeep,
      // Several screens use `outline` for muted text and icons.
      outline: MxColors.graphite,
      outlineVariant: MxColors.hairline,
      shadow: MxColors.ink,
      scrim: MxColors.ink,
      inverseSurface: MxColors.ink,
      onInverseSurface: Colors.white,
      inversePrimary: MxColors.irisSoft,
      surfaceTint: Colors.transparent,
    );

    // Every style states its tracking: Material's defaults (0.5 on bodyLarge, 0.25 on
    // bodyMedium) would otherwise merge in and space Hanken Grotesk out like a label.
    const text = TextTheme(
      displaySmall: TextStyle(fontSize: 34, height: 1.06, fontWeight: FontWeight.w600, letterSpacing: -1.1),
      headlineMedium: TextStyle(fontSize: 28, height: 1.1, fontWeight: FontWeight.w600, letterSpacing: -0.8),
      headlineSmall: TextStyle(fontSize: 22, height: 1.15, fontWeight: FontWeight.w600, letterSpacing: -0.4),
      titleLarge: TextStyle(fontSize: 18, height: 1.25, fontWeight: FontWeight.w600, letterSpacing: -0.2),
      titleMedium: TextStyle(fontSize: 16, height: 1.3, fontWeight: FontWeight.w600, letterSpacing: -0.1),
      titleSmall: TextStyle(fontSize: 14, height: 1.3, fontWeight: FontWeight.w600, letterSpacing: -0.05),
      bodyLarge: TextStyle(fontSize: 16, height: 1.45, fontWeight: FontWeight.w400, letterSpacing: -0.15),
      bodyMedium: TextStyle(fontSize: 14.5, height: 1.45, fontWeight: FontWeight.w400, letterSpacing: -0.1),
      bodySmall: TextStyle(fontSize: 13, height: 1.4, fontWeight: FontWeight.w400, letterSpacing: -0.05, color: MxColors.graphite),
      labelLarge: TextStyle(fontSize: 15, height: 1.2, fontWeight: FontWeight.w600, letterSpacing: -0.1),
      labelMedium: TextStyle(fontSize: 13, height: 1.2, fontWeight: FontWeight.w500, letterSpacing: 0),
      labelSmall: TextStyle(fontSize: 12, height: 1.2, fontWeight: FontWeight.w500, letterSpacing: 0.05),
    );

    final base = ThemeData(
      useMaterial3: true,
      colorScheme: scheme,
      fontFamily: MxType.family,
      textTheme: text,
    );

    const pill = StadiumBorder();
    const fieldRadius = BorderRadius.all(Radius.circular(MxRadii.md));

    return base.copyWith(
      scaffoldBackgroundColor: MxColors.canvas,
      splashFactory: InkSparkle.splashFactory,
      appBarTheme: const AppBarTheme(
        backgroundColor: Colors.transparent,
        surfaceTintColor: Colors.transparent,
        foregroundColor: MxColors.ink,
        elevation: 0,
        scrolledUnderElevation: 0,
        centerTitle: false,
        titleSpacing: 4,
        titleTextStyle: TextStyle(
          fontFamily: MxType.family,
          fontSize: 17,
          fontWeight: FontWeight.w600,
          color: MxColors.ink,
          letterSpacing: -0.2,
        ),
      ),
      cardTheme: const CardThemeData(
        color: MxColors.surface,
        surfaceTintColor: Colors.transparent,
        elevation: 0,
        margin: EdgeInsets.zero,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.all(Radius.circular(MxRadii.lg))),
      ),
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(
          minimumSize: const Size(64, 54),
          padding: const EdgeInsets.symmetric(horizontal: 24),
          shape: pill,
          textStyle: text.labelLarge?.copyWith(fontFamily: MxType.family),
        ),
      ),
      outlinedButtonTheme: OutlinedButtonThemeData(
        style: OutlinedButton.styleFrom(
          foregroundColor: MxColors.ink,
          minimumSize: const Size(64, 50),
          padding: const EdgeInsets.symmetric(horizontal: 20),
          shape: pill,
          side: const BorderSide(color: MxColors.hairline),
          textStyle: text.labelLarge?.copyWith(fontFamily: MxType.family),
        ),
      ),
      textButtonTheme: TextButtonThemeData(
        style: TextButton.styleFrom(
          foregroundColor: MxColors.ink,
          shape: pill,
          textStyle: text.labelLarge?.copyWith(fontFamily: MxType.family),
        ),
      ),
      iconButtonTheme: IconButtonThemeData(
        style: IconButton.styleFrom(foregroundColor: MxColors.ink),
      ),
      inputDecorationTheme: const InputDecorationTheme(
        filled: true,
        fillColor: MxColors.well,
        contentPadding: EdgeInsets.symmetric(horizontal: 18, vertical: 18),
        border: OutlineInputBorder(borderRadius: fieldRadius, borderSide: BorderSide.none),
        enabledBorder: OutlineInputBorder(borderRadius: fieldRadius, borderSide: BorderSide.none),
        disabledBorder: OutlineInputBorder(borderRadius: fieldRadius, borderSide: BorderSide.none),
        focusedBorder: OutlineInputBorder(
          borderRadius: fieldRadius,
          borderSide: BorderSide(color: MxColors.iris, width: 1.6),
        ),
        errorBorder: OutlineInputBorder(
          borderRadius: fieldRadius,
          borderSide: BorderSide(color: MxColors.redLine, width: 1.2),
        ),
        focusedErrorBorder: OutlineInputBorder(
          borderRadius: fieldRadius,
          borderSide: BorderSide(color: MxColors.red, width: 1.6),
        ),
        labelStyle: TextStyle(color: MxColors.graphite, fontWeight: FontWeight.w500, letterSpacing: -0.1),
        floatingLabelStyle: TextStyle(color: MxColors.graphite, fontWeight: FontWeight.w500),
        hintStyle: TextStyle(color: MxColors.mute, letterSpacing: -0.1),
        errorStyle: TextStyle(color: MxColors.red, fontSize: 12.5),
        prefixIconColor: MxColors.graphite,
        suffixIconColor: MxColors.graphite,
      ),
      // A selected filter chip is an ink pill, like the category chips in the references.
      chipTheme: ChipThemeData(
        color: WidgetStateProperty.resolveWith(
          (states) => states.contains(WidgetState.selected) ? MxColors.ink : MxColors.surface,
        ),
        side: WidgetStateBorderSide.resolveWith(
          (states) => BorderSide(
            color: states.contains(WidgetState.selected) ? MxColors.ink : MxColors.hairline,
          ),
        ),
        shape: pill,
        showCheckmark: false,
        labelStyle: TextStyle(
          fontFamily: MxType.family,
          fontSize: 13.5,
          fontWeight: FontWeight.w500,
          color: WidgetStateColor.resolveWith(
            (states) => states.contains(WidgetState.selected) ? Colors.white : MxColors.ink,
          ),
        ),
        padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 8),
      ),
      snackBarTheme: SnackBarThemeData(
        behavior: SnackBarBehavior.floating,
        backgroundColor: MxColors.ink,
        contentTextStyle: const TextStyle(
          fontFamily: MxType.family,
          color: Colors.white,
          fontSize: 14.5,
          fontWeight: FontWeight.w500,
        ),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(MxRadii.md)),
        insetPadding: const EdgeInsets.fromLTRB(16, 0, 16, 16),
      ),
      bottomSheetTheme: const BottomSheetThemeData(
        backgroundColor: MxColors.surface,
        surfaceTintColor: Colors.transparent,
        showDragHandle: true,
        dragHandleColor: MxColors.hairline,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.vertical(top: Radius.circular(MxRadii.xl)),
        ),
      ),
      dialogTheme: DialogThemeData(
        backgroundColor: MxColors.surface,
        surfaceTintColor: Colors.transparent,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(MxRadii.xl)),
        titleTextStyle: text.headlineSmall?.copyWith(
          fontFamily: MxType.family,
          color: MxColors.ink,
        ),
      ),
      listTileTheme: const ListTileThemeData(iconColor: MxColors.ink),
      dividerTheme: const DividerThemeData(color: MxColors.hairlineSoft, thickness: 1, space: 1),
      progressIndicatorTheme: const ProgressIndicatorThemeData(
        color: MxColors.ink,
        linearTrackColor: MxColors.wellDeep,
        circularTrackColor: Colors.transparent,
      ),
      // Yes/no and outcome pickers: a tall white pill pair, the chosen half filled in ink.
      // No checkmark — the fill says which is chosen, and nothing starts chosen.
      segmentedButtonTheme: SegmentedButtonThemeData(
        selectedIcon: const SizedBox.shrink(),
        style: SegmentedButton.styleFrom(
          backgroundColor: MxColors.surface,
          selectedBackgroundColor: MxColors.ink,
          selectedForegroundColor: Colors.white,
          foregroundColor: MxColors.ink,
          iconColor: MxColors.ink,
          minimumSize: const Size(0, 52),
          side: const BorderSide(color: MxColors.hairline),
          textStyle: text.labelLarge?.copyWith(fontFamily: MxType.family),
        ),
      ),
      dropdownMenuTheme: const DropdownMenuThemeData(
        menuStyle: MenuStyle(
          backgroundColor: WidgetStatePropertyAll(MxColors.surface),
          surfaceTintColor: WidgetStatePropertyAll(Colors.transparent),
        ),
      ),
    );
  }
}
