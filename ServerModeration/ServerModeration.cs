using UnityEngine;
using Steamworks;
using HarmonyLib;
using System.Collections.Generic;
using System;
using System.IO;
using HMLLibrary;
using RaftModLoader;

public class ServerModeration : Mod
{
    Harmony harmony;
    public static string worldConfigPath
    {
        get
        {
            return Path.Combine(SaveAndLoad.WorldPath, SaveAndLoad.WorldToLoad.name, "ServerModeration.json");
        }
    }
    public static string prefix = "[Server Moderation]: ";
    public void Start()
    {
        if (Raft_Network.IsHost && RAPI.GetLocalPlayer() != null)
            loadWorldJSON();
        harmony = new Harmony("com.aidanamite.ServerModeration");
        harmony.PatchAll();
        Debug.Log(prefix + "Mod has been loaded!");
    }
    public void OnModUnload()
    {
        harmony.UnpatchAll();
        Debug.Log(prefix + "Mod has been unloaded!");
    }

    public override void WorldEvent_WorldLoaded()
    {
        if (Raft_Network.IsHost)
            loadWorldJSON();
    }

    [ConsoleCommand(name: "kick", docs: "Syntax: 'kick <identifier>' Kicks a player from the server")]
    public static string MyCommand(string[] args)
    {
        if (RAPI.GetLocalPlayer() == null)
            return prefix + "This command can only be used in world";
        if (!Raft_Network.IsHost)
            return prefix + "This command can only be used by the host";
        if (args.Length < 1)
            return "Not enough parameters";
        if (args.Length > 1)
            return "Too many parameters";
        string kick = ComponentManager<Raft_Network>.Value.DisconnectUser(args[0]);
        if (kick == "")
            return prefix + "Could not find user with name \"" + args[0] + "\"";
        return prefix + "Kicked " + kick;
    }

    [ConsoleCommand(name: "ban", docs: "Syntax: 'ban <identifier>' Bans a player from the server")]
    public static string MyCommand2(string[] args)
    {
        if (RAPI.GetLocalPlayer() == null)
            return prefix + "This command can only be used in world";
        if (!Raft_Network.IsHost)
            return prefix + "This command can only be used by the host";
        if (args.Length < 1)
            return "Not enough parameters";
        if (args.Length > 1)
            return "Too many parameters";
        string kick = ComponentManager<Raft_Network>.Value.BanUser(args[0]);
        if (kick == "")
            return prefix + "Could not find user with name \"" + args[0] + "\"";
        saveWorldJSON();
        return prefix + "Banned " + kick;
    }

    [ConsoleCommand(name: "clearbans", docs: "Unbans all players")]
    public static string MyCommand3(string[] args)
    {
        if (RAPI.GetLocalPlayer() == null)
            return prefix + "This command can only be used in world";
        if (!Raft_Network.IsHost)
            return prefix + "This command can only be used by the host";
        ExtentionMethods.ClearBans();
        saveWorldJSON();
        return prefix + "Unbanned All";
    }

    [ConsoleCommand(name: "bans", docs: "Lists the names and IDs of the players in the ban list")]
    public static string MyCommand4(string[] args)
    {
        if (RAPI.GetLocalPlayer() == null)
            return prefix + "This command can only be used in world";
        if (!Raft_Network.IsHost)
            return prefix + "This command can only be used by the host";
        string names = "--- Banned Players:";
        foreach (KeyValuePair<CSteamID,string> pair in ExtentionMethods.blacklist)
            names += "\n" + pair.Value + " : " + pair.Key.m_SteamID;
        return prefix + names;
    }

    [ConsoleCommand(name: "unban", docs: "Syntax: 'unban <identifier>' Unbans a player")]
    public static string MyCommand5(string[] args)
    {
        if (RAPI.GetLocalPlayer() == null)
            return prefix + "This command can only be used in world";
        if (!Raft_Network.IsHost)
            return prefix + "This command can only be used by the host";
        if (args.Length < 1)
            return "Not enough parameters";
        if (args.Length > 1)
            return "Too many parameters";
        string user = ExtentionMethods.UnbanUser(args[0]);
        if (user == "")
            return prefix + "Could not find ban for \"" + args[0] + "\"";
        saveWorldJSON();
        return prefix + "Unbanned " + user;
    }

    [ConsoleCommand(name: "users", docs: "Lists the names and IDs of the players currently on the server")]
    public static string MyCommand6(string[] args)
    {
        if (RAPI.GetLocalPlayer() == null)
            return prefix + "This command can only be used in world";
        if (!Raft_Network.IsHost)
            return prefix + "This command can only be used by the host";
        string names = "--- Players:";
        foreach (KeyValuePair<CSteamID, Network_Player> pair in ComponentManager<Raft_Network>.Value.remoteUsers)
            names += "\n" + pair.Value.characterSettings.Name + " : " + pair.Key.m_SteamID;
        return prefix + names;
    }

    public static void loadWorldJSON()
    {
        JSONObject Config;
        try
        {
            Config = new JSONObject(File.ReadAllText(worldConfigPath));
        }
        catch
        {
            Config = new JSONObject();
        }
        ExtentionMethods.blacklist.Clear();
        if (!Config.IsNull)
            if (Config.HasField("bans"))
                foreach (KeyValuePair<string, string> pair in Config.GetField("bans").ToDictionary())
                    ExtentionMethods.blacklist.Add(new CSteamID(ulong.Parse(pair.Key)), pair.Value);
    }

    public static void saveWorldJSON()
    {
        Dictionary<string, string> data = new Dictionary<string, string>();
        foreach (KeyValuePair<CSteamID, string> pair in ExtentionMethods.blacklist)
            data.Add(pair.Key.m_SteamID.ToString(), pair.Value);
        JSONObject Config = new JSONObject();
        Config.AddField("bans", new JSONObject(data));
        try
        {
            File.WriteAllText(worldConfigPath, Config.ToString());
        }
        catch (Exception err)
        {
            Debug.LogError("An error occured while trying to save settings: " + err.Message);
        }
    }
}

static class ExtentionMethods
{
    public static Dictionary<CSteamID, string> blacklist = new Dictionary<CSteamID, string>();
    public static void DisconnectUser(this Raft_Network network, CSteamID steamID)
    {
        network.SendP2P(steamID, new Message_DisconnectNotify(Messages.Disconnect_Notify, network.LocalSteamID, DisconnectReason.HostDisconnected, true), EP2PSend.k_EP2PSendReliable, NetworkChannel.Channel_Session);
        network.RPCExclude(new Message_DisconnectNotify(Messages.Disconnect_Notify, steamID, DisconnectReason.SelfDisconnected, true), Target.Other, steamID, EP2PSend.k_EP2PSendReliable, NetworkChannel.Channel_Session);
        Traverse.Create(network).Method("OnDisconnect", new object[] { steamID, DisconnectReason.SelfDisconnected, true }).GetValue();
    }
    public static string DisconnectUser(this Raft_Network network, string Username)
    {
        KeyValuePair<CSteamID, Network_Player> pair = network.GetUser(Username);
        if (pair.Value == null)
            return "";
        network.DisconnectUser(pair.Key);
        return pair.Value.characterSettings.Name;
    }
    public static void BanUser(this Raft_Network network, CSteamID steamID)
    {
        blacklist.Add(steamID,network.GetPlayerFromID(steamID).characterSettings.Name);
        network.DisconnectUser(steamID);
    }
    public static string BanUser(this Raft_Network network, string Username)
    {
        KeyValuePair<CSteamID, Network_Player> pair = network.GetUser(Username);
        if (pair.Value == null)
            return "";
        network.BanUser(pair.Key);
        return pair.Value.characterSettings.Name;
    }
    public static KeyValuePair<CSteamID, Network_Player> GetUser(this Raft_Network network, string Username)
    {
        ulong id = 0;
        ulong.TryParse(Username, out id);
        Tuple<bool, bool> flags = GetFlags(ref Username);
        foreach (KeyValuePair<CSteamID, Network_Player> pair in network.remoteUsers)
            if (id == pair.Key.m_SteamID || Username.Compare(pair.Value.characterSettings.Name, flags.Item1, flags.Item2))
                return pair;
        return new KeyValuePair<CSteamID, Network_Player>(new CSteamID(0L),null);
    }
    public static void ClearBans()
    {
        blacklist.Clear();
    }
    public static bool IsBanned(CSteamID steamID)
    {
        foreach (CSteamID iD in blacklist.Keys)
            if (iD.m_SteamID == steamID.m_SteamID)
                return true;
        return false;
    }
    public static bool Compare(this string str, string checkstring, bool start, bool end)
    {
        if (start && end)
            return str.Contains(checkstring);
        else if (end)
            return str.StartsWith(checkstring);
        else if (start)
            return str.EndsWith(checkstring);
        else
            return str == checkstring;
    }
    public static Tuple<bool,bool> GetFlags(ref string str)
    {
        bool flag1 = false;
        bool flag2 = false;
        if (str[0] == '*')
        {
            flag1 = true;
            str = str.Substring(1);
        }
        if (str[str.Length - 1] == '*')
        {
            flag2 = true;
            str = str.Substring(0, str.Length - 1);
        }
        return new Tuple<bool, bool>(flag1, flag2);
    }
    public static string UnbanUser(string name)
    {
        ulong id = 0;
        ulong.TryParse(name, out id);
        Tuple<bool, bool> flags = GetFlags(ref name);
        foreach (KeyValuePair<CSteamID, string> pair in blacklist)
            if (id == pair.Key.m_SteamID || pair.Value.Compare(name, flags.Item1, flags.Item2))
            {
                blacklist.Remove(pair.Key);
                return pair.Value;
            }
        return "";
    }
}

[HarmonyPatch(typeof(Raft_Network), "CanUserJoinMe")]
public class Patch_ConnectionAttempt
{
    static void Postfix(ref Raft_Network __instance, ref InitiateResult __result, ref CSteamID remoteID)
    {
        if (__result == InitiateResult.Success && ExtentionMethods.IsBanned(remoteID))
            __result = InitiateResult.Fail_NotFriendWithHost;
    }
}

