import 'package:flutter/material.dart';

/// A labelled field that renders its own validation error.
///
/// Forms in this app validate with a `validate()` function returning a map of field name
/// to message, and pass the relevant entry in as [errorText] — the same shape the React
/// client uses, so the two stay consistent.
class AppFormField extends StatelessWidget {
  const AppFormField({
    super.key,
    required this.label,
    required this.controller,
    this.errorText,
    this.hintText,
    this.obscureText = false,
    this.keyboardType,
    this.maxLines = 1,
    this.enabled = true,
    this.onChanged,
    this.maxLength,
    this.suffixIcon,
    this.textInputAction,
    this.onSubmitted,
    this.autofillHints,
    this.fillColor,
  });

  final String label;
  final TextEditingController controller;
  final String? errorText;
  final String? hintText;
  final bool obscureText;
  final TextInputType? keyboardType;
  final int maxLines;
  final bool enabled;
  final ValueChanged<String>? onChanged;

  /// Shows a "12/100" counter and stops typing at the cap. Validate the length as well:
  /// the counter counts characters as the reader sees them, while the API counts UTF-16
  /// code units, so an emoji can pass one and fail the other.
  final int? maxLength;

  /// Display-only extras: a trailing control (a show-password eye), the keyboard's action
  /// key and what it does, and autofill hints for the platform's password manager.
  final Widget? suffixIcon;
  final TextInputAction? textInputAction;
  final ValueChanged<String>? onSubmitted;
  final Iterable<String>? autofillHints;

  /// The field's fill; the theme's grey when null. A field inside a grey panel is white.
  final Color? fillColor;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 14),
      child: TextField(
        textInputAction: textInputAction,
        onSubmitted: onSubmitted,
        autofillHints: autofillHints,
        maxLength: maxLength,
        controller: controller,
        obscureText: obscureText,
        keyboardType: keyboardType,
        maxLines: obscureText ? 1 : maxLines,
        enabled: enabled,
        onChanged: onChanged,
        decoration: InputDecoration(
          labelText: label,
          hintText: hintText,
          errorText: errorText,
          suffixIcon: suffixIcon,
          fillColor: fillColor,
          // No border here: the filled field and its focus ring come from AppTheme.
          alignLabelWithHint: maxLines > 1,
        ),
      ),
    );
  }
}

/// A labelled dropdown that renders its own validation error, so a picker and a text field
/// look the same in a form.
class AppDropdownField<T> extends StatelessWidget {
  const AppDropdownField({
    super.key,
    required this.label,
    required this.value,
    required this.items,
    required this.onChanged,
    this.errorText,
    this.hintText,
    this.fillColor,
  });

  final String label;
  final T? value;
  final List<DropdownMenuItem<T>> items;
  final ValueChanged<T?> onChanged;
  final String? errorText;
  final String? hintText;

  /// As on [AppFormField]: white inside a grey panel.
  final Color? fillColor;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 14),
      child: DropdownButtonFormField<T>(
        initialValue: value,
        items: items,
        onChanged: onChanged,
        isExpanded: true,
        borderRadius: BorderRadius.circular(16),
        decoration: InputDecoration(
          labelText: label,
          hintText: hintText,
          errorText: errorText,
          fillColor: fillColor,
        ),
      ),
    );
  }
}
