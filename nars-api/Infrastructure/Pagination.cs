namespace NarsApi.Infrastructure;

/// <summary>
/// Single pagination policy shared by every controller. Previously each
/// endpoint re-implemented the same skip/take clamp with three different
/// max page sizes (500/1000/2000), which drifted. One policy keeps response
/// sizes predictable and eliminates the duplicated boilerplate.
/// </summary>
public static class Pagination
{
    public const int MaxTake = 500;

    public static (int Skip, int Take) Clamp(int skip, int take) =>
        (Math.Max(skip, 0), Math.Clamp(take, 1, MaxTake));
}
