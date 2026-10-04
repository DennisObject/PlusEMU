using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Dapper;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Plus.Communication.Encryption;
using Plus.Communication.Flash;
using Plus.Communication.Http;
using Plus.Communication.Nitro;
using Plus.Communication.Packets.Outgoing.Moderation;
using Plus.Communication.RCON;
using Plus.Core;
using Plus.Core.FigureData;
using Plus.Core.Language;
using Plus.Core.Settings;
using Plus.Database;
using Plus.HabboHotel;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.UserData;

namespace Plus;

public class PlusEnvironment : IPlusEnvironment
{
    public const string PrettyVersion = "Plus Emulator";
    public const string PrettyBuild = "3.4.3.0";
    private static ILogger<PlusEnvironment> _logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<PlusEnvironment>.Instance;

    private static Encoding _defaultEncoding;
    public static CultureInfo CultureInfo;

    private static IGame _game;
    private static ILanguageManager _languageManager;
    private static ISettingsManager _settingsManager;
    private static IDatabase _database;
    private static IRconSocket _rcon;
    private static IFlashServer _flashServer;
    private readonly INitroServer _nitroServer;
    private static IAuthHttpServer _authHttpServer;
    private static IFigureDataManager _figureManager;
    private static IItemDataManager _itemDataManager;

    public static DateTime ServerStarted;

    private static readonly SearchValues<char> AllowedChars = SearchValues.Create("abcdefghijklmnopqrstuvwxyz1234567890-.");

    private static readonly ConcurrentDictionary<int, Habbo> _usersCached = new();

    private readonly IEnumerable<IStartable> _startableTasks;
    private readonly RconConfiguration _rconConfiguration;

    public PlusEnvironment(IDatabase database,
        ILanguageManager languageManager,
        ISettingsManager settingsManager,
        IFigureDataManager figureDataManager,
        IGame game,
        IEnumerable<IStartable> startableTasks,
        IRconSocket rconSocket,
        IOptions<RconConfiguration> rconConfiguration,
        IItemDataManager itemDataManager,
        IFlashServer flashServer,
        INitroServer nitroServer,
        IAuthHttpServer authHttpServer,
        ILogger<PlusEnvironment> logger)
    {
        _database = database;
        _languageManager = languageManager;
        _settingsManager = settingsManager;
        _figureManager = figureDataManager;
        _game = game;
        _startableTasks = startableTasks;
        _rcon = rconSocket;
        _flashServer = flashServer;
        _nitroServer = nitroServer;
        _authHttpServer = authHttpServer;
        _rconConfiguration = rconConfiguration.Value;
        _itemDataManager = itemDataManager;
        _logger = logger;
    }

    public async Task<bool> Start()
    {
        ServerStarted = DateTime.Now;
        Console.ForegroundColor = ConsoleColor.DarkGreen;
        Console.WriteLine();
        Console.WriteLine("                     ____  __           ________  _____  __");
        Console.WriteLine(@"                    / __ \/ /_  _______/ ____/  |/  / / / /");
        Console.WriteLine("                   / /_/ / / / / / ___/ __/ / /|_/ / / / / ");
        Console.WriteLine("                  / ____/ / /_/ (__  ) /___/ /  / / /_/ /  ");
        Console.WriteLine(@"                 /_/   /_/\__,_/____/_____/_/  /_/\____/ ");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"                                {PrettyVersion} <Build {PrettyBuild}>");
        Console.WriteLine("                                http://PlusIndustry.com");
        Console.WriteLine("");
        Console.Title = "Loading Plus Emulator";
        _defaultEncoding = Encoding.Default;
        Console.WriteLine("");
        Console.WriteLine("");
        CultureInfo = CultureInfo.InvariantCulture;
        try
        {
            if (!_database.IsConnected())
            {
                _logger.LogError("Failed to Connect to the specified MySQL server.");
                Console.ReadKey(true);
                return false;
            }
            _logger.LogInformation("Connected to Database!");

            //Reset our statistics first.
            await ResetStatistics();

            //Get the configuration & Game set.
            await _languageManager.Reload();
            await _settingsManager.Reload();
            _figureManager.Init();

            //Have our encryption ready.
            HabboEncryptionV2.Initialize(new());

            //Make sure Rcon is connected before we allow clients to Connect.
            _rcon.Init(_rconConfiguration.Hostname, _rconConfiguration.Port, _rconConfiguration.AllowedAddresses);

            _itemDataManager.Init();
            foreach (var task in _startableTasks.OrderBy(task => task.StartOrder))
                await task.Start();
            _flashServer.Start();
            _nitroServer.Start();
            await _authHttpServer.Start();
            _logger.LogInformation("Auth API listening on {Urls}", string.Join(", ", _authHttpServer.Urls));
            _game.StartGameLoop();
            var timeUsed = DateTime.Now - ServerStarted;
            Console.WriteLine();
            _logger.LogInformation("EMULATOR -> READY! ({Seconds} s, {Milliseconds} ms)", timeUsed.Seconds, timeUsed.Milliseconds);
        }
#pragma warning disable CS0168 // The variable 'e' is declared but never used
        catch (KeyNotFoundException e)
#pragma warning restore CS0168 // The variable 'e' is declared but never used
        {
            _logger.LogError("Please check your configuration file - some values appear to be missing.");
            _logger.LogError("Press any key to shut down ...");
            Console.ReadKey(true);
            return false;
        }
        catch (InvalidOperationException e)
        {
            _logger.LogError(e, "Failed to initialize PlusEmulator");
            _logger.LogError("Press any key to shut down ...");
            Console.ReadKey(true);
            return false;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Fatal error during startup");
            _logger.LogError("Press a key to exit");
            Console.ReadKey();
            return false;
        }

        return true;
    }

    private async Task ResetStatistics()
    {
        using var connection = _database.Connection();
        await connection.ExecuteAsync("TRUNCATE `catalog_marketplace_data`");
        await connection.ExecuteAsync("UPDATE `rooms` SET `users_now` = '0' WHERE `users_now` > '0';");
        await connection.ExecuteAsync("UPDATE `users` SET `online` = false WHERE `online` = true");
        await connection.ExecuteAsync("UPDATE `server_status` SET `users_online` = '0', `loaded_rooms` = '0'");
    }

    [Obsolete]
    public static bool EnumToBool(string @enum) => @enum == "1";

    [Obsolete]
    public static string BoolToEnum(bool @bool) => @bool ? "1" : "0";

    [Obsolete]
    public static double GetUnixTimestamp()
    {
        var ts = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0);
        return ts.TotalSeconds;
    }

    [Obsolete]
    public static long Now()
    {
        var ts = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0);
        var unixTime = ts.TotalMilliseconds;
        return (long)unixTime;
    }

    public static string FilterFigure(string figure)
    {
        foreach (var character in figure)
        {
            if (!IsValid(character))
                return "sh-3338-93.ea-1406-62.hr-831-49.ha-3331-92.hd-180-7.ch-3334-93-1408.lg-3337-92.ca-1813-62";
        }
        return figure;
    }

    private static bool IsValid(char character) => AllowedChars.Contains(character);

    [Obsolete($"Use {nameof(IUserDataFactory.GetUsernameForHabboById)}")]
    public static string GetUsernameById(int userId)
    {
        var name = "Unknown User";
        var client = Game.ClientManager.GetClientByUserId(userId);
        if (client != null && client.GetHabbo() != null)
            return client.GetHabbo().Username;
        var user = Game.CacheManager.GenerateUser(userId);
        if (user != null)
            return user.Username;
        using (var dbClient = DatabaseManager.GetQueryReactor())
        {
            dbClient.SetQuery("SELECT `username` FROM `users` WHERE `id` = @id LIMIT 1");
            dbClient.AddParameter("id", userId);
            name = dbClient.GetString();
        }
        if (string.IsNullOrEmpty(name))
            name = "Unknown User";
        return name;
    }

    [Obsolete("Use GameClientManager instead")]
    public static Habbo? GetHabboById(int userId)
    {
        try
        {
            var client = Game.ClientManager.GetClientByUserId(userId);
            if (client != null)
            {
                var user = client.GetHabbo();
                if (user != null && user.Id > 0)
                {
                    if (_usersCached.ContainsKey(userId))
                        _usersCached.TryRemove(userId, out user);
                    return user;
                }
            }
            else
            {
                try
                {
                    if (_usersCached.ContainsKey(userId))
                        return _usersCached[userId];
                    //var data = UserDataFactory.GetUserData(userId);
                    //if (data != null)
                    //{
                    //    var generated = data.User;
                    //    if (generated != null)
                    //    {
                    //        generated.InitInformation(data);
                    //        _usersCached.TryAdd(userId, generated);
                    //        return generated;
                    //    }
                    //}
                }
                catch
                {
                    return null;
                }
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    public static Habbo? GetHabboByUsername(string userName)
    {
        try
        {
            using var dbClient = DatabaseManager.GetQueryReactor();
            dbClient.SetQuery("SELECT `id` FROM `users` WHERE `username` = @user LIMIT 1");
            dbClient.AddParameter("user", userName);
            var id = dbClient.GetInteger();
            if (id > 0)
                return GetHabboById(Convert.ToInt32(id));
            return null;
        }
        catch
        {
            return null;
        }
    }


    public static void PerformShutDown()
    {
        Console.Clear();
        _logger.LogInformation("Server shutting down...");
        Console.Title = "PLUS EMULATOR: SHUTTING DOWN!";
        // No new logins while the hotel goes down.
        _authHttpServer.Stop().Wait(TimeSpan.FromSeconds(5));
        Game.ClientManager.SendPacket(new BroadcastMessageAlertComposer(LanguageManager.TryGetValue("server.shutdown.message")));
        Game.StopGameLoop();
        Thread.Sleep(2500);
        _flashServer.Stop();
        Game.ClientManager.CloseAll(); //Close all connections
        Game.RoomManager.Dispose(); //Stop the game loop.
        if (!Debugger.IsAttached)
        {
            using var dbClient = _database.GetQueryReactor();
            dbClient.RunQuery("TRUNCATE `catalog_marketplace_data`");
            dbClient.RunQuery("UPDATE `users` SET `online` = false, `auth_ticket` = '', `auth_ticket_expires_at` = NULL");
            dbClient.RunQuery("UPDATE `rooms` SET `users_now` = '0' WHERE `users_now` > '0'");
            dbClient.RunQuery("UPDATE `server_status` SET `users_online` = '0', `loaded_rooms` = '0'");
        }
        _logger.LogInformation("Plus Emulator has successfully shutdown.");
        Thread.Sleep(1000);
        Environment.Exit(0);
    }

    public static Encoding GetDefaultEncoding() => _defaultEncoding;

    [Obsolete("Use dependency injection instead and inject required services.")]
    public static IGame Game => _game;

    public static IRconSocket RconSocket => _rcon;

    public static IFigureDataManager FigureManager => _figureManager;

    [Obsolete("Inject IDatabase instead")]
    public static IDatabase DatabaseManager => _database;

    public static ILanguageManager LanguageManager => _languageManager;

    public static ISettingsManager SettingsManager => _settingsManager;

    public static ICollection<Habbo> CachedUsers => _usersCached.Values;

    public static bool RemoveFromCache(int id, out Habbo data) => _usersCached.TryRemove(id, out data);
}
