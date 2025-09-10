using Microsoft.Extensions.Logging;
using CommunityToolkit.Maui;

namespace mindvault;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder.UseMauiApp<App>()
               .UseMauiCommunityToolkit()
               .ConfigureFonts(fonts =>
               {
                   fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                   fonts.AddFont("fa-solid-900.otf", "FAS"); 
               });

#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
