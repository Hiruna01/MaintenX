import 'dart:typed_data';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:image_picker/image_picker.dart';

/// The API's limits on a report photo (`ImageUploadRules`): JPEG or PNG, at most 5 MB.
///
/// Mirrored here only so the reporter hears about a bad photo when they pick it, not after
/// the report is filed. The API is still the rule — it also checks the file's first bytes,
/// which is the check that makes the content type worth anything.
const int maxPhotoBytes = 5 * 1024 * 1024;
const Set<String> allowedPhotoTypes = {'image/jpeg', 'image/png'};

/// A photo picked and read into memory, ready to upload once the report exists.
class PickedPhoto {
  const PickedPhoto({required this.bytes, required this.contentType});

  final Uint8List bytes;
  final String contentType;

  String get sizeLabel => '${(bytes.length / (1024 * 1024)).toStringAsFixed(1)} MB';

  /// The content type for [file]: what the platform reported, else read off the file
  /// extension. Null for anything that is not a JPEG or a PNG.
  static String? contentTypeOf(XFile file) {
    final reported = file.mimeType?.toLowerCase();
    if (reported != null) {
      final normalised = reported == 'image/jpg' ? 'image/jpeg' : reported;
      return allowedPhotoTypes.contains(normalised) ? normalised : null;
    }
    final name = file.name.toLowerCase();
    if (name.endsWith('.jpg') || name.endsWith('.jpeg')) return 'image/jpeg';
    if (name.endsWith('.png')) return 'image/png';
    return null;
  }

  /// Reads [file] and checks it against the limits. Returns the photo, or throws a
  /// [PhotoRejected] saying why in words a reporter can act on.
  static Future<PickedPhoto> fromFile(XFile file) async {
    final contentType = contentTypeOf(file);
    if (contentType == null) {
      throw const PhotoRejected('Only JPEG or PNG photos can be attached.');
    }
    final bytes = await file.readAsBytes();
    if (bytes.length > maxPhotoBytes) {
      throw PhotoRejected(
        'That photo is ${(bytes.length / (1024 * 1024)).toStringAsFixed(1)} MB; '
        'the limit is ${maxPhotoBytes ~/ (1024 * 1024)} MB.',
      );
    }
    return PickedPhoto(bytes: bytes, contentType: contentType);
  }
}

class PhotoRejected implements Exception {
  const PhotoRejected(this.message);

  final String message;

  @override
  String toString() => message;
}

/// The picker, as a provider so a test can hand the screen a fake one — a test has no
/// camera and no gallery.
final imagePickerProvider = Provider<ImagePicker>((ref) => ImagePicker());
