export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  /** Present on endpoints that compute it server-side (e.g. notifications history). */
  totalPages?: number;
}
