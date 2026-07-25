using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace PveModeHarmony
{
    /// <summary>
    /// English messages ported from the Oxide PveMode LoadDefaultMessages (en locale only).
    /// Stored/loaded from HarmonyLanguage/PveMode.json so server admins can edit wording.
    /// </summary>
    public static class PveModeLang
    {
        private static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>
        {
            ["NoLootScientist"] = "You <color=#ce3f27>are unable</color> to loot this NPC due to another player doing more damage!",
            ["NoLootCrateEvent"] = "You <color=#ce3f27>cannot</color> loot the crate! You are not the Event Owner and you are not on their team!",
            ["NoHackCrateEvent"] = "You <color=#ce3f27>cannot</color> hack the locked crate! You are not the Event Owner and you are not on their team!",
            ["NoLootScientistEvent"] = "You <color=#ce3f27>cannot</color> loot an NPC's corpse! You are not the Event Owner and you are not on their team!",
            ["NoDamageTankEvent"] = "You <color=#ce3f27>cannot</color> damage Bradley! You are not the Event Owner and you are not on their team!",
            ["NoDamageHelicopterEvent"] = "You <color=#ce3f27>cannot</color> damage Patrol Helicopter! You are not the Event Owner and you are not on their team!",
            ["NoDamageTurretEvent"] = "You <color=#ce3f27>cannot</color> damage Turret! You are not the Event Owner and you are not on their team!",
            ["NoDamageNpcEvent"] = "You <color=#ce3f27>cannot</color> damage NPC! You are not the Event Owner and you are not on their team!",
            ["NoEnterEvent"] = "You <color=#ce3f27>cannot</color> enter the Event zone! You are not the Event Owner and you are not on their team!",
            ["YouOwnerEvent"] = "You are now the <color=#738d43>Event Owner</color>!",
            ["ChangeOwnerEventToFriend"] = "You have exited the <color=#ce3f27>Event Zone</color>. The <color=#738d43>Event owner</color> is now <color=#55aaff>{0}</color>",
            ["TimerStartEvent"] = "You <color=#ce3f27>have left</color> the Event zone. You have to return to the Event zone in <color=#55aaff>{0}</color> or you will lose Event Owner status",
            ["AlertTimerEvent"] = "You have <color=#55aaff>{0}</color> to return to the Event Zone and keep Event Owner status",
            ["YouNonOwnerEvent"] = "You <color=#ce3f27>lost</color> the Event Owner status!",
            ["NoCanActionEvent"] = "You <color=#ce3f27>cannot</color> perform this action! You are not the Event Owner and you are not on their team!",
            ["OwnerEndEvent"] = "Event <color=#55aaff>{0}</color> is over. You were the Event Owner. You can play this event no earlier than in <color=#55aaff>{1}</color>",
            ["PlayerHasCooldownEnter"] = "You have <color=#ce3f27>entered</color> the event area in which you <color=#ce3f27>cannot</color> become the owner (you may <color=#ce3f27>lose loot</color>), you <color=#ce3f27>still have</color> a timer for participation in this event. You must wait at least <color=#55aaff>{0}</color> to become owner of this event again",
            ["EventsTime"] = "List of events:\n(If the event is not in the list, then you can become its owner)\n(If the event is marked with <color=#55aaff>*</color>, it means that it is currently active and the cooldown is indicated to get the status of event owner. Otherwise, it is indicated how long ago you were the owner of the event)"
        };

        private static Dictionary<string, string> _messages;

        public static void Load(string path)
        {
            _messages = new Dictionary<string, string>(Defaults);
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    Dictionary<string, string> loaded = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                    if (loaded != null)
                    {
                        foreach (KeyValuePair<string, string> kv in loaded) _messages[kv.Key] = kv.Value;
                    }
                }
                else
                {
                    Save(path);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PveMode] Failed to load language from " + path + ": " + ex.Message + ". Using defaults.");
            }
        }

        public static void Save(string path)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, JsonConvert.SerializeObject(_messages ?? Defaults, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PveMode] Failed to save language to " + path + ": " + ex.Message);
            }
        }

        public static string Get(string key)
        {
            if (_messages != null && _messages.TryGetValue(key, out string msg)) return msg;
            return Defaults.TryGetValue(key, out string def) ? def : key;
        }

        public static string Get(string key, params object[] args)
        {
            if (args == null || args.Length == 0) return Get(key);
            try { return string.Format(Get(key), args); }
            catch { return Get(key); }
        }
    }
}
