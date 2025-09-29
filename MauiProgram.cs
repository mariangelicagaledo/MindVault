using Microsoft.Extensions.Logging;
using CommunityToolkit.Maui;
using mindvault.Services; // DatabaseService etc.

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

        builder.Services.AddSingleton(sp =>
        {
            var dbPath = Path.Combine(FileSystem.AppDataDirectory, "mindvault.db3");
            var db = new DatabaseService(dbPath);
            Task.Run(() => db.InitializeAsync()).Wait();
            return db;
        });

        builder.Services.AddSingleton<MultiplayerService>();

        // Register ONLY the quantized T5 service
        builder.Services.AddSingleton<T5FlashcardService>(_ => T5FlashcardService.Create());
        builder.Services.AddSingleton<FlashcardGenerator>();

#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
