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

        public IQueryable<T> Paginate(IQueryable<T> queryable, int pageNumber, int pageSize)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = _defaultPageSize;
            if (pageSize > _maxPageSize) pageSize = _maxPageSize;

            return queryable
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize);
        }
    }
}
