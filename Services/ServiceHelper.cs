using Microsoft.Extensions.DependencyInjection;

namespace mindvault.Services;

public static class ServiceHelper
{
    public static IServiceProvider Services =>
        Application.Current?.Handler?.MauiContext?.Services
        ?? throw new InvalidOperationException("Service provider not available");

    public static T GetRequiredService<T>() where T : notnull => Services.GetRequiredService<T>();
}
