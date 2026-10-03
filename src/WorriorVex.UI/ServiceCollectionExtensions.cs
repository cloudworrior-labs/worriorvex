using Microsoft.Extensions.DependencyInjection;
using WorriorVex.UI.Localization;

namespace WorriorVex.UI;

public static class ServiceCollectionExtensions
{
    /// <summary>Services the shared interface needs, whatever hosts it.</summary>
    public static IServiceCollection AddWorriorVexUI(this IServiceCollection services)
    {
        services.AddSingleton<Translator>();
        return services;
    }
}
