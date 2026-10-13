using ACA360.Web.Helpers;

namespace ACA360.Tests.Unit.Helpers;

public class GridPagingTests
{
    [Theory]
    [InlineData(0, 0, 1, 10)]       // nothing sent
    [InlineData(-3, -1, 1, 10)]
    [InlineData(1, 10, 1, 10)]
    [InlineData(4, 25, 4, 25)]
    [InlineData(2, 100, 2, 100)]    // the largest size the grids offer
    [InlineData(1, 100000, 1, 500)] // capped
    public void PagingIsAlwaysUsable(int page, int size, int expectedPage, int expectedSize) =>
        Assert.Equal((expectedPage, expectedSize), GridPaging.Normalise(page, size));
}
