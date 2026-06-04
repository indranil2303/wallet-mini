namespace wallet.domain.contracts;
public sealed record PagedRecord<T>(IReadOnlyList<T>? Items, int Page, int PageSize, long TotalRecords, int TotalPages, bool HasNextPage, bool HasPreviousPage);