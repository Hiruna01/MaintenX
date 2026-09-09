/// Mirrors the API's RoomDto. Only the fields the picker needs are kept.
class Room {
  const Room({
    required this.id,
    required this.buildingId,
    required this.name,
    required this.code,
    required this.floor,
  });

  final int id;
  final int buildingId;
  final String name;
  final String code;
  final int floor;

  factory Room.fromJson(Map<String, dynamic> json) {
    return Room(
      id: json['id'] as int,
      buildingId: json['buildingId'] as int,
      name: json['name'] as String,
      code: json['code'] as String,
      floor: json['floor'] as int,
    );
  }

  String get label => '$code — $name';
}
