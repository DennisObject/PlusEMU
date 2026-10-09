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
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.UserData;

namespace Plus;

public class PlusEnvironment : IPlusEnvironment
{
    public const string PrettyVersion = "Plus Emulator";
    public const string PrettyBuild = "3.4.3.0";
    private static ILogger<PlusEnvironment>? _logger;
    private static ILogger<PlusEnvironment> Logger => _logger ?? throw new InvalidOperationException("The environment has not been constructed.");

    private static Encoding _defaultEncoding = Encoding.Default;
    public static CultureInfo CultureInfo;

    private static IGame _game;
    private static IGameClientManager? _clientManager;
    private static IGameClientManager Clients => _clientManager ?? throw new InvalidOperationException("The environment has not been constructed.");
    private static IRoomManager? _roomManager;
    private static IRoomManager Rooms => _roomManager ?? throw new InvalidOperationException("The environment has not been constructed.");
    private static ILanguageManager _languageManager;
    private static ISettingsManager _settingsManager;
    private static IDatabase _database;
    private static IRconSocket _rcon;
    private static IFlashServer _flashServer;
    private readonly INitroServer _nitroServer;
    private static IAuthHttpServer _authHttpServer;
    private static IFigureDataManager _figureManager;
    private static IItemDataManager _itemDataManager;

    private readonly IServerUptime _uptime;

    private static readonly SearchValues<char> AllowedChars = SearchValues.Create("abcdefghijklmnopqrstuvwxyz1234567890-.");

    private static readonly ConcurrentDictionary<int, Habbo> _usersCached = new();

    private readonly IEnumerable<IStartable> _startableTasks;
    private readonly RconConfiguration _rconConfiguration;

    public PlusEnvironment(IDatabase database,
        ILanguageManager languageManager,
        ISettingsManager settingsManager,
        IFigureDataManager figureDataManager,
        IGame game,
        IGameClientManager clientManager,
        IRoomManager roomManager,
        IEnumerable<IStartable> startableTasks,
        IRconSocket rconSocket,
        IOptions<RconConfiguration> rconConfiguration,
        IItemDataManager itemDataManager,
        IFlashServer flashServer,
        INitroServer nitroServer,
        IAuthHttpServer authHttpServer,
        ILogger<PlusEnvironment> logger, IServerUptime uptime)
    {
        _database = database;
        _languageManager = languageManager;
        _settingsManager = settingsManager;
        _figureManager = figureDataManager;
        _game = game;
        _clientManager = clientManager;
        _roomManager = roomManager;
        _startableTasks = startableTasks;
        _rcon = rconSocket;
        _flashServer = flashServer;
        _nitroServer = nitroServer;
        _authHttpServer = authHttpServer;
        _rconConfiguration = rconConfiguration.Value;
        _itemDataManager = itemDataManager;
        _logger = logger;
        _uptime = uptime;
    }

    public async Task<bool> Start()
    {
        _uptime.Start();
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
        ConsoleWindow.SetTitle("Loading Plus Emulator");
        Console.WriteLine("");
        Console.WriteLine("");
        CultureInfo = CultureInfo.InvariantCulture;

        try {
            if (!_database.IsConnected()) {
                Logger.LogError("Failed to Connect to the specified MySQL server.");
                ConsoleWindow.WaitForKey();

                return false;
            }

            Logger.LogInformation("Connected to Database!");

            //Reset our statistics first.
            await ResetStatistics();

            //Get the configuration & Game set.
            await _languageManager.Reload();
            await _settingsManager.Reload();

            //Have our encryption ready.
            HabboEncryptionV2.Initialize(new());

            await StartupSequence.Start(_startableTasks);

            // Managers are ready before any listener accepts requests.
            _rcon.Init(_rconConfiguration.Hostname, _rconConfiguration.Port, _rconConfiguration.AllowedAddresses);
            _flashServer.Start();
            _nitroServer.Start();
            await _authHttpServer.Start();
            Logger.LogInformation("Auth API listening on {Urls}", string.Join(", ", _authHttpServer.Urls));
            _game.StartGameLoop();
            var timeUsed = _uptime.Elapsed;
            Console.WriteLine();
            Logger.LogInformation("EMULATOR -> READY! ({Seconds} s, {Milliseconds} ms)", timeUsed.Seconds, timeUsed.Milliseconds);
        }
#pragma warning disable CS0168 // The variable 'e' is declared but never used
        catch (KeyNotFoundException e)
#pragma warning restore CS0168 // The variable 'e' is declared but never used
        {
            Logger.LogError("Please check your configuration file - some values appear to be missing.");
            Logger.LogError("Press any key to shut down ...");
            ConsoleWindow.WaitForKey();

            return false;
        }
        catch (InvalidOperationException e) {
            Logger.LogError(e, "Failed to initialize PlusEmulator");
            Logger.LogError("Press any key to shut down ...");
            ConsoleWindow.WaitForKey();

            return false;
        }
        catch (Exception e) {
            Logger.LogError(e, "Fatal error during startup");
            Logger.LogError("Press a key to exit");
            ConsoleWindow.WaitForKey();

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
    public static bool EnumToBool(string @enum) => @enum == "1" || bool.TryParse(@enum, out var boolean) && boolean;

    [Obsolete]
    public static string BoolToEnum(bool @bool) => @bool ? "1" : "0";

    public static string FilterFigure(string figure)
    {
        foreach (var character in figure) {
            if (!IsValid(character)) {
                return "sh-3338-93.ea-1406-62.hr-831-49.ha-3331-92.hd-180-7.ch-3334-93-1408.lg-3337-92.ca-1813-62";
            }
        }

        return figure;
    }

    private static bool IsValid(char character) => AllowedChars.Contains(character);

    [Obsolete($"Use {nameof(IUserDataFactory.GetUsernameForHabboById)}")]
    public static string GetUsernameById(int userId)
    {
        var name = "Unknown User";
        var client = Game.ClientManager.GetClientByUserId(userId);

        if (client != null && client.GetHabbo() != null) {
            return client.GetHabbo().Username;
        }

        var user = Game.CacheManager.GenerateUser(userId);

        if (user != null) {
            return user.Username;
        }

        using (var connection = DatabaseManager.Connection()) {
            name = connection.QuerySingleOrDefault<string>("SELECT username FROM users WHERE id=@userId LIMIT 1", new { userId });
        }

        if (string.IsNullOrEmpty(name)) {
            name = "Unknown User";
        }

        return name;
    }

    [Obsolete("Use GameClientManager instead")]
    public static Habbo? GetHabboById(int userId) => FindHabbo(userId);

    private static Habbo? FindHabbo(int userId)
    {
        try {
            var user = Clients.GetClientByUserId(userId)?.GetHabbo();

            if (user is { Id: > 0 }) {
                _usersCached.TryRemove(userId, out _);

                return user;
            }

            return _usersCached.TryGetValue(userId, out var cached) ? cached : null;
        }
        catch {
            return null;
        }
    }

    public static Habbo? GetHabboByUsername(string userName)
    {
        try {
            using var connection = _database.Connection();
            var id = connection.QuerySingleOrDefault<int>("SELECT id FROM users WHERE username=@userName LIMIT 1", new { userName });

            if (id > 0) {
                return FindHabbo(Convert.ToInt32(id));
            }

            return null;
        }
        catch {
            return null;
        }
    }


    public static void PerformShutDown()
    {
        ConsoleWindow.Clear();
        Logger.LogInformation("Server shutting down...");
        ConsoleWindow.SetTitle("PLUS EMULATOR: SHUTTING DOWN!");
        // No new logins while the hotel goes down.
        _authHttpServer.Stop().Wait(TimeSpan.FromSeconds(5));
        Clients.SendPacket(new BroadcastMessageAlertComposer(LanguageManager.TryGetValue("server.shutdown.message")));
        _game.StopGameLoop();
        Thread.Sleep(2500);
        _flashServer.Stop();
        Clients.CloseAll(); //Close all connections
        Rooms.Dispose(); //Stop the game loop.

        if (!Debugger.IsAttached) {
            using var connection = _database.Connection();
            connection.Execute("TRUNCATE catalog_marketplace_data");
            connection.Open();
            using var transaction = connection.BeginTransaction();
            connection.Execute("UPDATE users SET online=false,auth_ticket='',auth_ticket_expires_at=NULL", transaction: transaction);
            connection.Execute("UPDATE rooms SET users_now=0 WHERE users_now>0", transaction: transaction);
            connection.Execute("UPDATE server_status SET users_online=0,loaded_rooms=0", transaction: transaction);
            transaction.Commit();
        }

        Logger.LogInformation("Plus Emulator has successfully shutdown.");
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

    public static bool RemoveFromCache(int id, out Habbo? data) => _usersCached.TryRemove(id, out data);

    internal static IReadOnlyList<Habbo> RemoveExpiredCachedUsers(DateTimeOffset now)
    {
        List<Habbo> removed = [];

        foreach (var entry in _usersCached.ToArray()) {
            if (entry.Value.CacheExpiredAt(now) && ((ICollection<KeyValuePair<int, Habbo>>)_usersCached).Remove(entry)) {
                removed.Add(entry.Value);
            }
        }

        return removed;
    }
}
