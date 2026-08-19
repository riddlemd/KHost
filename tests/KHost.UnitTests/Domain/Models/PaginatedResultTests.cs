using KHost.Abstractions.Models;

namespace KHost.UnitTests.Domain.Models;

public class PaginatedResultTests
{
    [Fact]
    public void DefaultConstruction_ProducesEmptyResult()
    {
        var result = new PaginatedResult<string>();

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.PageNumber);
        Assert.Equal(0, result.PageSize);
    }

    [Theory]
    [InlineData(100, 10, 10)]
    [InlineData(101, 10, 11)]
    [InlineData(0, 10, 0)]
    [InlineData(5, 10, 1)]
    [InlineData(50, 50, 1)]
    public void TotalPages_ComputesFromCountAndPageSize(int totalCount, int pageSize, int expected)
    {
        var result = new PaginatedResult<string> { TotalCount = totalCount, PageSize = pageSize };

        Assert.Equal(expected, result.TotalPages);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void TotalPages_IsZero_WhenPageSizeIsNotPositive(int pageSize)
    {
        var result = new PaginatedResult<string> { TotalCount = 5, PageSize = pageSize, PageNumber = 1 };

        Assert.Equal(0, result.TotalPages);
    }

    [Fact]
    public void HasNextPage_DoesNotThrow_WhenPageSizeIsZero()
    {
        var result = new PaginatedResult<string> { TotalCount = 5, PageSize = 0, PageNumber = 1 };

        Assert.False(result.HasNextPage);
    }

    [Fact]
    public void DefaultConstruction_TotalPagesDoesNotThrow()
    {
        var result = new PaginatedResult<string>();

        Assert.Equal(0, result.TotalPages);
    }

    [Fact]
    public void HasPreviousPage_IsFalse_OnFirstPage()
    {
        var result = new PaginatedResult<string> { PageNumber = 1, PageSize = 10, TotalCount = 50 };

        Assert.False(result.HasPreviousPage);
    }

    [Fact]
    public void HasPreviousPage_IsTrue_WhenNotOnFirstPage()
    {
        var result = new PaginatedResult<string> { PageNumber = 2, PageSize = 10, TotalCount = 50 };

        Assert.True(result.HasPreviousPage);
    }

    [Fact]
    public void HasNextPage_IsTrue_WhenMorePagesRemain()
    {
        var result = new PaginatedResult<string> { PageNumber = 1, PageSize = 10, TotalCount = 50 };

        Assert.True(result.HasNextPage);
    }

    [Fact]
    public void HasNextPage_IsFalse_OnLastPage()
    {
        var result = new PaginatedResult<string> { PageNumber = 5, PageSize = 10, TotalCount = 50 };

        Assert.False(result.HasNextPage);
    }

}
