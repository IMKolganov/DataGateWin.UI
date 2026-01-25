using System.Windows.Controls;
using DataGateWin.Controllers;
using Wpf.Ui.Abstractions;

namespace DataGateWin.Pages.Home;

public sealed class CachedPageProvider : INavigationViewPageProvider, IDisposable
{
    private readonly Dictionary<Type, Page> _cache = new();

    private readonly HomeController _homeController = new();

    public object? GetPage(Type pageType)
    {
        if (_cache.TryGetValue(pageType, out var page))
            return page;

        page = CreatePage(pageType);
        _cache[pageType] = page;
        return page;
    }

    private Page CreatePage(Type pageType)
    {
        if (pageType == typeof(HomePage))
            return new HomePage(_homeController);

        // other pages (no caching required, but we can still cache them if you want)
        return (Page)Activator.CreateInstance(pageType)!;
    }

    public void Dispose()
    {
        _homeController.Dispose();
    }
}