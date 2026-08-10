using KHost.Abstractions.Models;

namespace KHost.DataAccess.Repositories.Components
{
    internal class PaginationComponent<T>
        where T : class
    {
        private readonly int _maxPageSize;
        private readonly int _defaultPageSize;
        public PaginationComponent(int maxPageSize, int defaultPageSize)
        {
            _maxPageSize = maxPageSize;
            _defaultPageSize = defaultPageSize;
        }

        public (int PageNumber, int PageSize) Normalize(int pageNumber, int pageSize)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = _defaultPageSize;
            if (pageSize > _maxPageSize) pageSize = _maxPageSize;

            return (pageNumber, pageSize);
        }

        public IQueryable<T> Paginate(IQueryable<T> queryable, int pageNumber, int pageSize)
        {
            var (page, size) = Normalize(pageNumber, pageSize);

            return queryable
                .Skip((page - 1) * size)
                .Take(size);
        }

        // Callers must build results through here: the requested page values are clamped before
        // the query runs, so reporting the raw request describes a page that was never fetched.
        public PaginatedResult<T> BuildResult(List<T> items, int totalCount, int pageNumber, int pageSize)
        {
            var (page, size) = Normalize(pageNumber, pageSize);

            return new PaginatedResult<T>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = page,
                PageSize = size
            };
        }
    }
}
