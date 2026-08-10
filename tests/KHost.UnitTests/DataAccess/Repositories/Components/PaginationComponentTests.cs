using KHost.DataAccess.Repositories.Components;

namespace KHost.UnitTests.DataAccess.Repositories.Components;

public class PaginationComponentTests
{
    private const int MaxPageSize = 100;
    private const int DefaultPageSize = 25;

    private static PaginationComponent<string> MakeComponent()
        => new(MaxPageSize, DefaultPageSize);

    private static IQueryable<string> MakeItems(int count)
        => Enumerable.Range(1, count).Select(i => $"item{i}").AsQueryable();

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(1, 1)]
    [InlineData(7, 7)]
    public void Normalize_ClampsPageNumberToAtLeastOne(int requested, int expected)
    {
        var (pageNumber, _) = MakeComponent().Normalize(requested, 10);

        Assert.Equal(expected, pageNumber);
    }

    [Theory]
    [InlineData(0, DefaultPageSize)]
    [InlineData(-1, DefaultPageSize)]
    [InlineData(10, 10)]
    [InlineData(MaxPageSize, MaxPageSize)]
    [InlineData(MaxPageSize + 1, MaxPageSize)]
    [InlineData(5000, MaxPageSize)]
    public void Normalize_ClampsPageSizeIntoRange(int requested, int expected)
    {
        var (_, pageSize) = MakeComponent().Normalize(1, requested);

        Assert.Equal(expected, pageSize);
    }

    [Fact]
    public void Paginate_UsesDefaultPageSize_WhenPageSizeIsZero()
    {
        var result = MakeComponent().Paginate(MakeItems(200), pageNumber: 1, pageSize: 0).ToList();

        Assert.Equal(DefaultPageSize, result.Count);
        Assert.Equal("item1", result[0]);
    }

    [Fact]
    public void Paginate_CapsAtMaxPageSize()
    {
        var result = MakeComponent().Paginate(MakeItems(500), pageNumber: 1, pageSize: 5000).ToList();

        Assert.Equal(MaxPageSize, result.Count);
    }

    [Fact]
    public void Paginate_SkipsUsingClampedPageNumber_WhenPageNumberIsZero()
    {
        var result = MakeComponent().Paginate(MakeItems(50), pageNumber: 0, pageSize: 10).ToList();

        Assert.Equal("item1", result[0]);
    }

    [Fact]
    public void Paginate_ReturnsRequestedPage()
    {
        var result = MakeComponent().Paginate(MakeItems(50), pageNumber: 3, pageSize: 10).ToList();

        Assert.Equal(10, result.Count);
        Assert.Equal("item21", result[0]);
    }

    [Fact]
    public void BuildResult_ReportsEffectivePageSize_NotRequested()
    {
        var result = MakeComponent().BuildResult(["a"], totalCount: 200, pageNumber: 1, pageSize: 0);

        Assert.Equal(DefaultPageSize, result.PageSize);
    }

    [Fact]
    public void BuildResult_ReportsCappedPageSize_WhenRequestExceedsMax()
    {
        var result = MakeComponent().BuildResult(["a"], totalCount: 200, pageNumber: 1, pageSize: 5000);

        Assert.Equal(MaxPageSize, result.PageSize);
    }

    [Fact]
    public void BuildResult_ReportsClampedPageNumber()
    {
        var result = MakeComponent().BuildResult(["a"], totalCount: 200, pageNumber: 0, pageSize: 10);

        Assert.Equal(1, result.PageNumber);
    }

    [Fact]
    public void BuildResult_ProducesConsistentTotalPages_WhenPageSizeDefaulted()
    {
        var result = MakeComponent().BuildResult(["a"], totalCount: 200, pageNumber: 1, pageSize: 0);

        // 200 items at the default 25 per page — meaningless if PageSize reported the raw 0.
        Assert.Equal(8, result.TotalPages);
        Assert.True(result.HasNextPage);
    }
}
