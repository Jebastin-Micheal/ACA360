namespace ACA360.Web.Helpers
{
    /// <summary>
    /// Paging values for the list grids. The grids always send a page and a page size (10 to 100),
    /// but a request without them binds 0, which the list procedures reject ("FETCH ... must be
    /// greater than zero") and the page answered with a server error.
    /// </summary>
    public static class GridPaging
    {
        public const int DefaultPageSize = 10;
        public const int MaxPageSize = 500;

        /// <summary>Page 1 when missing or below 1; the default size when missing, capped at MaxPageSize.</summary>
        public static (int PageIndex, int PageSize) Normalise(int pageIndex, int pageSize) =>
            (Math.Max(1, pageIndex), pageSize < 1 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize));
    }
}
