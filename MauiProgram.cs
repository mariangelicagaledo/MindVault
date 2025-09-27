using Microsoft.Extensions.Logging;
using CommunityToolkit.Maui;
using mindvault.Services; // restored for DatabaseService & MultiplayerService

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

        // Register SQLite-backed DatabaseService as singleton
        builder.Services.AddSingleton(sp =>
        {
            var dbPath = Path.Combine(FileSystem.AppDataDirectory, "mindvault.db3");
            var db = new DatabaseService(dbPath);
            Task.Run(() => db.InitializeAsync()).Wait();
            return db;
        });

        // Multiplayer services
        builder.Services.AddSingleton<MultiplayerService>();
        // OfflineNerQuestionService removed

#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
