using System.Collections.Immutable;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.AI;

namespace Plus.HabboHotel.Items.Wired.Modern.Actions;

public static class WiredBotActions
{
    public static readonly IReadOnlySet<string> Names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { "wf_act_bot_teleport", "wf_act_bot_move", "wf_act_bot_follow_avatar", "wf_act_bot_give_handitem", "wf_act_bot_talk", "wf_act_bot_talk_to_avatar", "wf_act_bot_clothes" };
    public static WiredConfiguration Defaults(string name) => new()
    {
        IntParams = name switch
        {
            "wf_act_bot_teleport" or "wf_act_bot_move" => [100, 100], "wf_act_bot_follow_avatar" => [0, 0, 100],
            "wf_act_bot_give_handitem" => [0, 0, 100], "wf_act_bot_talk" => [0, 100, -1],
            "wf_act_bot_talk_to_avatar" => [0, 0, 100, -1], "wf_act_bot_clothes" => [100],
            _ => throw new ArgumentException("Unknown bot action.", nameof(name))
        }, Text = name is "wf_act_bot_talk" or "wf_act_bot_talk_to_avatar" or "wf_act_bot_clothes" ? "\t" : ""
    };
    public static bool TryValidate(string name, WiredConfiguration proposed, out WiredConfiguration validated, out string error)
    {
        validated = proposed; error = "Invalid bot configuration.";
        if (!Names.Contains(name) || !WiredLegacyProtocol.IsWithinLimits(proposed)) return false;
        var p = proposed.IntParams; var f = ImmutableDictionary.CreateBuilder<string, int>(); var u = ImmutableDictionary.CreateBuilder<string, int>();
        bool Source(int index) => p[index] is 0 or 100 or 200 or 201;
        bool User(int index) => p[index] is 0 or 10 or 11 or 200 or 201;
        switch (name)
        {
            case "wf_act_bot_teleport": case "wf_act_bot_move":
                if (p.Length != 2 || !Source(0) || !Source(1)) return false; f["items"] = p[0]; u["bots"] = p[1]; break;
            case "wf_act_bot_follow_avatar": case "wf_act_bot_give_handitem":
                if (p.Length != 3 || p[0] < 0 || name == "wf_act_bot_follow_avatar" && p[0] > 1 || !User(1) || !Source(2)) return false;
                u["users"] = p[1]; u["bots"] = p[2]; break;
            case "wf_act_bot_talk":
                if (p.Length != 3 || p[0] is < 0 or > 1 || !Source(1) || p[2] is < -1 or > 2) return false; u["bots"] = p[1]; break;
            case "wf_act_bot_talk_to_avatar":
                if (p.Length != 4 || p[0] is < 0 or > 1 || !User(1) || !Source(2) || p[3] is < -1 or > 2) return false;
                u["users"] = p[1]; u["bots"] = p[2]; break;
            case "wf_act_bot_clothes":
                if (p.Length != 1 || !Source(0)) return false; u["bots"] = p[0]; break;
        }
        if (proposed.Text.Split('\t', 2)[0].Length > 64) return false;
        validated = proposed with { FurniSources = f.ToImmutable(), UserSources = u.ToImmutable() }; error = ""; return true;
    }
    public static bool Execute(string name, WiredRuntimeContext context, WiredConfiguration config,
        WiredRoomMovement movement, IBotManagementStore botStore)
    {
        if (!TryValidate(name, config, out config, out _)) return false;
        var parts = config.Text.Split('\t', 2); var botName = parts[0]; var text = parts.Length > 1 ? parts[1] : "";
        var source = config.UserSources["bots"];
        var bots = source == 0 ? context.Event.Actor is { IsBot: true, IsPet: false } actor
                && context.Targets.AllUsers().Any(user => ReferenceEquals(user, actor)) ? new[] { actor } : []
            : context.Targets.ResolveUsers(context, [], source, botName).Where(user => user.IsBot && !user.IsPet).ToArray();
        var optionalUnnamedHandItem = name == "wf_act_bot_give_handitem" && source == 0 && botName.Length == 0;
        if (bots.Length == 0 && !optionalUnnamedHandItem) return false;
        var users = config.UserSources.TryGetValue("users", out var userSource)
            ? context.Targets.ResolveUsers(context, [], userSource).Where(user => !user.IsBot).ToArray() : [];
        var items = config.FurniSources.TryGetValue("items", out var furniSource) ? context.Targets.ResolveFurni(context, config.SelectedItems, furniSource) : [];
        var p = config.IntParams; var changed = false; var targets = WiredBotTargets.For(context.Room);
        switch (name)
        {
            case "wf_act_bot_teleport": case "wf_act_bot_move":
                if (items.Length == 0) return false;
                foreach (var bot in bots)
                {
                    var item = items[Random.Shared.Next(items.Length)];
                    if (name == "wf_act_bot_teleport")
                    {
                        WiredAvatarState.For(context.Room).Thaw(bot, teleport: true);
                        changed |= movement.MoveAvatar(context, bot, item.GetX, item.GetY, false);
                    }
                    else
                    {
                        bot.BotData.ForcedUserTargetMovement = 0; bot.BotData.ForcedMovement = true;
                        bot.BotData.TargetCoordinate = new(item.GetX, item.GetY); bot.MoveTo(item.GetX, item.GetY);
                        targets.Walk(bot, item); changed = true;
                    }
                }
                return changed;
            case "wf_act_bot_follow_avatar":
                if (p[0] == 0) { foreach (var bot in bots) targets.Stop(bot); return true; }
                if (users.Length == 0) return false;
                for (var index = 0; index < bots.Length; index++)
                {
                    var bot = bots[index]; var user = users[index % users.Length];
                    bot.BotData.ForcedMovement = false; bot.BotData.ForcedUserTargetMovement = user.HabboId;
                    targets.Follow(bot, user);
                }
                foreach (var arrived in targets.Poll(context.Room)) context.Room.GetWired().Dispatch(arrived);
                return true;
            case "wf_act_bot_give_handitem":
                // Turbo's executable body grants immediately after resolving the named bot.
                foreach (var user in users) user.CarryItem(p[0]); return users.Length > 0;
            case "wf_act_bot_clothes":
                if (!FigureWellFormed(text)) return false;
                foreach (var bot in bots)
                {
                    botStore.SaveAppearance(bot.BotData.Id, context.Room.RoomId, text, bot.BotData.Gender);
                    bot.BotData.Look = text;
                    context.Room.SendPacket(new UserChangeComposer(AvatarChangeSnapshot.Capture(bot.BotData)));
                }
                return true;
            case "wf_act_bot_talk": case "wf_act_bot_talk_to_avatar":
                if (text.Length == 0) return false;
                if (name == "wf_act_bot_talk_to_avatar" && users.Length == 0) return false;
                foreach (var bot in bots)
                {
                    var formatted = context.Policy.FormatText(context, text.Replace("%name%", bot.BotData.Name, StringComparison.Ordinal));
                    var width = p[name == "wf_act_bot_talk" ? 2 : 3];
                    if (name == "wf_act_bot_talk_to_avatar" && p[0] == 1)
                    {
                        foreach (var user in users) { var client = user.GetClient(); if (client == null) continue; client.Send(new WiredChatComposer(bot.VirtualId, formatted, bot.BotData.ChatBubble, width, true)); changed = true; }
                    }
                    else { context.Room.SendPacket(new WiredChatComposer(bot.VirtualId, formatted, bot.BotData.ChatBubble, width, false, name == "wf_act_bot_talk" && p[0] == 1)); changed = true; }
                }
                return changed;
            default: throw new InvalidOperationException("No bot executor.");
        }
    }
    public static bool FigureWellFormed(string figure) => figure.Length is > 0 and <= 512 && figure.Split('.').All(part =>
    {
        var fields = part.Split('-'); return fields.Length >= 2 && fields[0].Length == 2 && fields[0].All(c => c is >= 'a' and <= 'z')
            && fields.Skip(1).All(field => field.Length > 0 && field.All(c => c is >= '0' and <= '9'));
    });
}
