/// Mirrors the API's `PagedResult<T>` — the one pagination shape every list endpoint
/// returns. Only paging counters; it carries no success or error information.
class PagedResult<T> {
  const PagedResult({
    required this.items,
    required this.page,
    required this.pageSize,
    required this.totalCount,
    required this.totalPages,
  });

  final List<T> items;
  final int page;
  final int pageSize;
  final int totalCount;
  final int totalPages;

  bool get hasPrevious => page > 1;
  bool get hasNext => page < totalPages;

  factory PagedResult.fromJson(
    Map<String, dynamic> json,
    T Function(Map<String, dynamic> item) fromItem,
  ) {
    return PagedResult(
      items: (json['items'] as List<dynamic>)
          .map((item) => fromItem(item as Map<String, dynamic>))
          .toList(growable: false),
      page: json['page'] as int,
      pageSize: json['pageSize'] as int,
      totalCount: json['totalCount'] as int,
      totalPages: json['totalPages'] as int,
    );
  }
}
