import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:image_picker/image_picker.dart';

/// The API's limits on a photo upload (`ImageUploadRules`): JPEG or PNG, at most 5 MB. One
/// set of rules on the server for a report photo and a completion photo, so one here too.
///
/// Mirrored here only so the user hears about a bad photo when they pick it, not after the
/// report is filed or the job completed. The API is still the rule — it also checks the
/// file's first bytes, which is the check that makes the content type worth anything.
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

/// Asks for the camera or the gallery, picks, and checks the result against the limits.
///
/// The one photo picker in the app — the report form and the completion form both call it.
/// Returns null when the user cancels, which is not an error; throws [PhotoRejected] with a
/// message they can act on for a file the API would refuse or a permission that is off.
Future<PickedPhoto?> choosePhoto(BuildContext context, ImagePicker picker) async {
  final source = await showModalBottomSheet<ImageSource>(
    context: context,
    showDragHandle: true,
    builder: (context) => SafeArea(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          ListTile(
            leading: const Icon(Icons.photo_camera_outlined),
            title: const Text('Take a photo'),
            onTap: () => Navigator.pop(context, ImageSource.camera),
          ),
          ListTile(
            leading: const Icon(Icons.photo_library_outlined),
            title: const Text('Choose from gallery'),
            onTap: () => Navigator.pop(context, ImageSource.gallery),
          ),
        ],
      ),
    ),
  );
  if (source == null) return null;

  try {
    // Scaled and re-encoded on the phone: a fault photo does not need 48 megapixels, and
    // this keeps it well inside the API's 5 MB. With a quality set, iOS also re-encodes a
    // HEIC photo as JPEG, which is one of the two types the API accepts.
    final file = await picker.pickImage(
      source: source,
      maxWidth: 2048,
      maxHeight: 2048,
      imageQuality: 85,
    );
    if (file == null) return null; // Cancelled — not an error.

    return await PickedPhoto.fromFile(file);
  } on PlatformException catch (error) {
    throw PhotoRejected(switch (error.code) {
      'camera_access_denied' =>
        'Camera access is off. Allow it in Settings, or choose from the gallery.',
      'photo_access_denied' =>
        'Photo access is off. Allow it in Settings, or take a photo instead.',
      _ => 'Could not open the camera or gallery.',
    });
  }
}

/// The picker, as a provider so a test can hand the screen a fake one — a test has no
/// camera and no gallery.
final imagePickerProvider = Provider<ImagePicker>((ref) => ImagePicker());
