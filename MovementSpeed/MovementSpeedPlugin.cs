using Facepunch;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

/* To-do
 *​‌​​‌‌​‌​‌​‌​‌​​​‌‌​​​‌‌​‌‌‌‌​​​​‌​​‌‌‌​​‌‌​​‌‌‌​​‌‌‌‌​‌​​‌‌‌‌​‌ Look at converting to the movement speed modifier system.
 * 
 */

/* 1.0.9
 * Updated how velocity is impelemented. Capped airborn velicity at 6.2m/s. I do not recommend going above 10 speed.
 */

namespace Oxide.Plugins
{
	[Info("MovementSpeed", "imthenewguy", "1.0.9")]
	[Description("Permission based run and swim speeds")]
	public partial class MovementSpeed : RustPlugin
	{
        #region Config       

        private Configuration config;
        public class Configuration
        {
			[JsonProperty("How often should the behaviour check to see if the player is running? [lower = smoother run]")]
			public float BehaviourInterval = 0.05f;

            [JsonProperty("Allow the speed effect to work when the player is wounded?")]
            public bool Allow_Wounded = false;

            [JsonProperty("Disable speed while at events [SurvivalArena, Paintball, ZombieInfection, GunGame]")]
            public bool Disable_At_Events = true;

            [JsonProperty("Should the movement buff be applied at all times while moving?")]
            public bool Always_running = false;

            [JsonProperty("Allow the movement buff to be applied while crouching?")]
            public bool Allow_while_crouching = true;

            [JsonProperty("Command settings")]
            public CommandsInfo commands = new CommandsInfo();

            [JsonProperty("Run speed permissions and multipliers [perm name: multiplier]", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, float> run_permissions = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
			{
				["movementspeed.run.1.5"] = 1.5f,
				["movementspeed.run.2"] = 2,
				["movementspeed.run.3"] = 3,
				["movementspeed.run.4"] = 4,
				["movementspeed.run.5"] = 5,
			};

            [JsonProperty("Swim speed permissions and multipliers [perm name: multiplier]", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, float> swim_permissions = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
            {
                ["movementspeed.swim.1.5"] = 1.5f,
                ["movementspeed.swim.2"] = 2,
                ["movementspeed.swim.3"] = 3,
                ["movementspeed.swim.4"] = 4,
                ["movementspeed.swim.5"] = 5,
            };

            [JsonProperty("Zone Manager Settings")]
            public ZoneManagerSettings zoneManagerSettings = new ZoneManagerSettings();

            public string ToJson() => JsonConvert.SerializeObject(this);

            public Dictionary<string, object> ToDictionary() => JsonConvert.DeserializeObject<Dictionary<string, object>>(ToJson());
        }

        public class ZoneManagerSettings
        {
            [JsonProperty("List of zone names that we should pause movement speed in?")]
            public List<string> pause_zones = new List<string>();
        }

        public class CommandsInfo
        {
            [JsonProperty("Command to toggle run speed")]
            public string toggleRunCMD = "togglerun";

            [JsonProperty("Command to toggle swim speed")]
            public string toggleSwimCMD = "toggleswim";
        }
        protected override void LoadDefaultConfig() => config = new Configuration();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null)
                {
                    throw new JsonException();
                }

                SaveConfig();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogException(ex);
                Interface.Oxide.UnloadPlugin(Name);
            }
        }

        protected override void SaveConfig()
        {
            PrintToConsole($"Configuration changes saved to {Name}.json");
            Config.WriteObject(config, true);
        }

        #endregion

        #region Hooks

        public static MovementSpeed Instance;
        public const string perm_admin = "movementspeed.admin";
        void Init()
        {
            Instance = this;
            if (!string.IsNullOrEmpty(config.commands.toggleRunCMD)) cmd.AddChatCommand(config.commands.toggleRunCMD, this, nameof(ToggleRunCMD));
            if (!string.IsNullOrEmpty(config.commands.toggleSwimCMD)) cmd.AddChatCommand(config.commands.toggleSwimCMD, this, nameof(ToggleSwimCMD));
            permission.RegisterPermission(perm_admin, this);
            if (!config.Disable_At_Events)
            {
                Unsubscribe(nameof(OnEventJoined));
                Unsubscribe(nameof(OnEventLeave));
            }
        }

        void OnServerInitialized(bool initial)
		{
			for (int i = config.swim_permissions.Count -  1; i >= 0; i--)
			{
				var kvp = config.swim_permissions.ElementAt(i);

                if (kvp.Key.StartsWith("MovementSpeed.", StringComparison.OrdinalIgnoreCase)) continue;
				config.swim_permissions.Add($"{this.Name}.{kvp.Key}", kvp.Value);
				config.swim_permissions.Remove(kvp.Key);
			}

            for (int i = config.run_permissions.Count - 1; i >= 0; i--)
            {
                var kvp = config.run_permissions.ElementAt(i);

                if (kvp.Key.StartsWith("MovementSpeed.", StringComparison.OrdinalIgnoreCase)) continue;
                config.run_permissions.Add($"{this.Name}.{kvp.Key}", kvp.Value);
                config.run_permissions.Remove(kvp.Key);
            }

			foreach (var perm in config.run_permissions.Keys)
				if (!permission.PermissionExists(perm)) permission.RegisterPermission(perm, this);

            foreach (var perm in config.swim_permissions.Keys)
                if (!permission.PermissionExists(perm)) permission.RegisterPermission(perm, this);

            foreach (var player in BasePlayer.activePlayerList)
			{
                DisableStaleRunBehaviours(player);
				SetupSpeedBoosts(player);
            }

            if (config.zoneManagerSettings.pause_zones.Count == 0)
            {
                Unsubscribe(nameof(OnEnterZone));
                Unsubscribe(nameof(OnExitZone));
            }
        }

        void OnPlayerConnected(BasePlayer player)
        {
            SetupSpeedBoosts(player);
        }

        void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            RemoveSpeedComponent(player.userID, true);            
        }

        void SetupSpeedBoosts(BasePlayer player)
		{
			if (!HasSwimMovementSpeeds(player.UserIDString, out var swim, out var speed)) return;
            AddSpeedComponent(player, swim, speed);
        }

		bool HasSwimMovementSpeeds(string userid, out float swim, out float run)
		{
            run = 0;
			swim = 0;
            foreach (var perm in config.swim_permissions)
                if (permission.UserHasPermission(userid, perm.Key) && perm.Value > swim) swim = perm.Value;

            foreach (var perm in config.run_permissions)
                if (permission.UserHasPermission(userid, perm.Key) && perm.Value > run) run = perm.Value;

            if (PluginMods.TryGetValue(ulong.Parse(userid), out var mods))
            {
                foreach (var mod in mods)
                {
                    if (mod.runSpeed > run) run = mod.runSpeed;
                    if (mod.swimSpeed > swim) swim = mod.swimSpeed;
                }
            }

            return run > 0 || swim > 0;
		}

		void OnGroupPermissionGranted(string name, string perm)
		{
            string logInfo = $"** OnGroupPermissionGranted for {name} **";
            try
            {
                if (!config.swim_permissions.ContainsKey(perm) && !config.run_permissions.ContainsKey(perm))
                {
                    logInfo += "\n- Perm is not a swim or run perm.";
                    return;
                }
                logInfo += "\n- Cycling through players to create the behaviour.";
                foreach (var user in permission.GetUsersInGroup(name))
                {
                    OnUserPermissionGranted(user.Split(' ')[0], perm);
                }
            }
            finally
            {
                LogToFile("PermsDebug", logInfo, this, true, true);
            }
            
        }

		void OnGroupPermissionRevoked(string name, string perm)
		{
            string logInfo = $"** OnGroupPermissionRevoked for {name} **";
            try
            {
                if (!config.swim_permissions.ContainsKey(perm) && !config.run_permissions.ContainsKey(perm))
                {
                    logInfo += "\n- Perm is not a swim or run perm.";
                    return;
                }
                logInfo += "\n- Cycling through players to revoke the behaviour.";
                foreach (var user in permission.GetUsersInGroup(name))
                    OnUserPermissionRevoked(user.Split(' ')[0], perm);

            }
            finally
            {
                LogToFile("PermsDebug", logInfo, this, true, true);
            }
        }


		void OnUserGroupAdded(string id, string groupName)
		{
            string logInfo = $"** OnUserGroupAdded for {id} **";
            try
            {
                float run = 0;
                float swim = 0;

                foreach (var kvp in config.swim_permissions)
                {
                    if (!permission.GroupHasPermission(groupName, kvp.Key)) continue;
                    if (kvp.Value > swim) swim = kvp.Value;
                }

                foreach (var kvp in config.run_permissions)
                {
                    if (!permission.GroupHasPermission(groupName, kvp.Key)) continue;
                    if (kvp.Value > run) run = kvp.Value;
                }

                logInfo += $"\n- Swim: {swim}. Run: {run}";

                if (!ulong.TryParse(id, out var userid))
                {
                    logInfo += $"\n- Could not parse id: {id}";
                    return;
                }
                if (!BasePlayer.TryFindByID(userid, out var player) || !player.IsConnected)
                {
                    logInfo += $"\n- {userid} isnt connected or doesnt have a baseplayer.";
                    return;
                }

                if (!Components.TryGetValue(userid, out var behaviour))
                {
                    logInfo += $"\n- Setting up new behaviour for {userid}";
                    SetupSpeedBoosts(player);
                    return;
                }

                if (behaviour.GetRunVelocity() < run)
                {
                    logInfo += $"\n- Updating run for {userid} because the current value is lower";
                    behaviour.SetRunVelocity(run);
                }
                if (behaviour.GetSwimVelocity() < swim)
                {
                    logInfo += $"\n- Updating swim for {userid} because the current value is lower";
                    behaviour.SetSwimVelocity(swim);
                }
            }
            finally
            {
                LogToFile("PermsDebug", logInfo, this, true, true);
            }
            	
        }


		void OnUserGroupRemoved(string id, string groupName)
		{
            string logInfo = $"** OnUserGroupRemoved for {id} **";
            try
            {
                float run = 0;
                float swim = 0;

                foreach (var kvp in config.swim_permissions)
                {
                    if (!permission.GroupHasPermission(groupName, kvp.Key)) continue;
                    if (kvp.Value > swim) swim = kvp.Value;
                }

                foreach (var kvp in config.run_permissions)
                {
                    if (!permission.GroupHasPermission(groupName, kvp.Key)) continue;
                    if (kvp.Value > run) run = kvp.Value;
                }

                logInfo += $"\n- User: {id} run: {run}. Swim: {swim}";

                if (!ulong.TryParse(id, out var userid))
                {
                    logInfo += $"\n- Failed to parse: {id}";
                    return;
                }
                if (!BasePlayer.TryFindByID(userid, out var player) || !player.IsConnected)
                {
                    logInfo += $"\n- {userid} isnt connected or doesnt have a baseplayer.";
                    return;
                }
                if (!Components.TryGetValue(userid, out var behaviour))
                {
                    logInfo += $"\n- {id} doesnt have a behaviour stored.";
                    return;
                }

                if (behaviour.GetRunVelocity() < run && behaviour.GetSwimVelocity() < swim)
                {
                    logInfo += $"\n- Doing nothing because current run and swim is less than proposed run and swim.";
                    return;
                }

                if (!HasSwimMovementSpeeds(id, out var currentSwim, out var currentRun))
                {
                    logInfo += $"\n- Removing behaviour";
                    RemoveSpeedComponent(userid);
                    return;
                }

                logInfo += $"\n- Setting values: run: {currentRun}. Swim: {currentSwim}";
                behaviour.SetSwimVelocity(currentSwim);
                behaviour.SetRunVelocity(currentRun);
            }
            finally
            {
                LogToFile("PermsDebug", logInfo, this, true, true);
            }
        }

		void OnUserPermissionGranted(string id, string permName)
		{
            var perm = permName.ToLower();
            config.run_permissions.TryGetValue(perm, out var run);
            config.swim_permissions.TryGetValue(perm, out var swim);

            if (run == 0 && swim == 0)
            {
                return;
            }
            if (!ulong.TryParse(id, out var userid))
            {
                return;
            }
            if (!BasePlayer.TryFindByID(userid, out var player) || !player.IsConnected)
            {
                return;
            }
            if (!Components.TryGetValue(userid, out var behaviour))
            {
                AddSpeedComponent(player, swim, run);
                return;
            }

            if (behaviour.GetSwimVelocity() < swim)
            {
                behaviour.SetSwimVelocity(swim);
            }
            if (behaviour.GetRunVelocity() < run)
            {
                behaviour.SetRunVelocity(run);
            }
        }

		void OnUserPermissionRevoked(string id, string permName)
		{
			if (!permName.StartsWith(this.Name, StringComparison.OrdinalIgnoreCase)) return;

            if (!ulong.TryParse(id, out var userid)) return;
            if (!BasePlayer.TryFindByID(userid, out var player) || !player.IsConnected) return;
            if (!Components.TryGetValue(userid, out var behaviour)) return;

            if (!HasSwimMovementSpeeds(id, out var currentSwim, out var currentRun))
            {
                RemoveSpeedComponent(userid);
                return;
            }

            behaviour.SetSwimVelocity(currentSwim);
            behaviour.SetRunVelocity(currentRun);
        }

        void Unload()
        {
            // Do not Destroy RunBehaviour on unload: a pre-update RunBehaviour.OnDestroy still
            // calls the removed ResetAntiHack(int, speed, fly) overload and throws MissingMethodException
            // (Unity defers OnDestroy until after the replacement mod has already loaded).
            foreach (var player in BasePlayer.activePlayerList)
                DetachSpeedComponentsSafe(player);

            foreach (var sleeping in BasePlayer.sleepingPlayerList)
                DetachSpeedComponentsSafe(sleeping);

            cmd.RemoveChatCommand(config.commands.toggleRunCMD, this);
            cmd.RemoveChatCommand(config.commands.toggleSwimCMD, this);

            Components.Clear();
            PluginMods.Clear();
        }

        /// <summary>
        /// Disables and strips MovementSpeed behaviours without invoking fragile OnDestroy antihack resets.
        /// </summary>
        static void DetachSpeedComponentsSafe(BasePlayer player)
        {
            if (player == null || player.IsDestroyed) return;
            try
            {
                player.ApplyInheritedVelocity(Vector3.zero);
                player.PauseSpeedHackDetection(0.01f);
            }
            catch { }

            MonoBehaviour[] behaviours;
            try { behaviours = player.GetComponents<MonoBehaviour>(); }
            catch { return; }

            for (int i = 0; i < behaviours.Length; i++)
            {
                var mb = behaviours[i];
                if (mb == null) continue;
                if (mb.GetType().Name != "RunBehaviour" && mb.name != "MovementSpeedMod") continue;
                try
                {
                    if (mb is RunBehaviour rb)
                        rb.SuppressAntiHackReset = true;
                    mb.enabled = false;
                    // DestroyImmediate runs OnDestroy now (with suppress flag for current assembly).
                    // Stale foreign RunBehaviour types: DestroyImmediate still hits old OnDestroy — skip Destroy for those.
                    if (mb is RunBehaviour)
                        UnityEngine.Object.DestroyImmediate(mb);
                }
                catch { }
            }
        }

        #endregion

        #region Behaviour

        Dictionary<ulong, RunBehaviour> Components = new Dictionary<ulong, RunBehaviour>();

		void AddSpeedComponent(BasePlayer player, float swim, float run)
		{
            Puts($"Setting up behaviour for {player.displayName} [{player.userID}] - Run: {run} - Swim: {swim}");
            DisableStaleRunBehaviours(player);
			if (!Components.TryGetValue(player.userID, out var component) || component == null)
            {
                component = player.gameObject.AddComponent<RunBehaviour>();
                component.name = "MovementSpeedMod";
                component.Interval = config.BehaviourInterval;
                if (InSpeedLimitZone(player)) component.PauseSpeed(true);
                Components[player.userID] = component;
            }
            component.SetRunVelocity(run);

            component.SetSwimVelocity(swim);
        }

        /// <summary>Disable leftover RunBehaviour instances from a previously loaded MovementSpeed assembly.</summary>
        static void DisableStaleRunBehaviours(BasePlayer player)
        {
            if (player == null) return;
            MonoBehaviour[] behaviours;
            try { behaviours = player.GetComponents<MonoBehaviour>(); }
            catch { return; }
            for (int i = 0; i < behaviours.Length; i++)
            {
                var mb = behaviours[i];
                if (mb == null || mb is RunBehaviour) continue;
                if (mb.GetType().Name != "RunBehaviour") continue;
                try { mb.enabled = false; } catch { }
            }
        }

        bool InSpeedLimitZone(BasePlayer player)
        {
            if (ZoneManager == null || !ZoneManager.IsLoaded) return false;
            string[] zoneIds = ZoneManager.Call("GetPlayerZoneIDs", player) as string[];
            if (zoneIds == null) return false;
            foreach (var zone in zoneIds)
            {
                string name = ZoneManager.Call("GetZoneName", zone) as string;
                if (string.IsNullOrEmpty(name)) continue;
                if (config.zoneManagerSettings.pause_zones.Contains(name))
                    return true;
            }                

            return false;
        }

        void RemoveSpeedComponent(ulong id, bool removeFromList)
        {
            if (Components.TryGetValue(id, out var behav))
            {
                if (behav != null) GameObject.Destroy(behav);
                if (removeFromList) Components.Remove(id);
            }

            if (!PluginMods.TryGetValue(id, out var Mods)) return;
            foreach (var mod in Mods)
                mod.Destroy();

            Mods.Clear();

            if (removeFromList) PluginMods.Remove(id);
        }

        void SetRunspeed(BasePlayer player, float mod)
        {
            DisableStaleRunBehaviours(player);
            if (!Components.TryGetValue(player.userID, out var component) || component == null)
            {
                component = player.gameObject.AddComponent<RunBehaviour>();
                component.name = "MovementSpeedMod";
                component.Interval = config.BehaviourInterval;
                if (InSpeedLimitZone(player)) component.PauseSpeed(true);
                Components[player.userID] = component;
            }

            if (component.GetRunVelocity() < mod)
            {
                component.SetRunVelocity(mod);
            }
        }

        void SetSwimspeed(BasePlayer player, float mod)
        {
            DisableStaleRunBehaviours(player);
            if (!Components.TryGetValue(player.userID, out var component) || component == null)
            {
                component = player.gameObject.AddComponent<RunBehaviour>();
                component.name = "MovementSpeedMod";
                component.Interval = config.BehaviourInterval;
                if (InSpeedLimitZone(player)) component.PauseSpeed(true);
                Components[player.userID] = component;
            }
            
            if (component.GetSwimVelocity() < mod)
            {
                component.SetSwimVelocity(mod);
            }
        }


        void RemoveSpeedComponent(ulong id)
		{
			if (!Components.TryGetValue(id, out var component)) return;
            if (component != null)
            {
                component.SuppressAntiHackReset = false;
                GameObject.Destroy(component);
            }
			Components.Remove(id);
		}

        void ToggleRunCMD(BasePlayer player)
        {
            if (!Components.TryGetValue(player.userID, out var component)) return;
            component.runEnabled = !component.runEnabled;
            PrintToChat(player, $"Toggled run: {component.runEnabled}");
        }

        void ToggleSwimCMD(BasePlayer player)
        {
            if (!Components.TryGetValue(player.userID, out var component)) return;
            component.swimEnabled = !component.swimEnabled;
            PrintToChat(player, $"Toggled run: {component.swimEnabled}");
        }

        [ConsoleCommand("msdisablerun")]
        void DisableRunCMD(ConsoleSystem.Arg arg)
        {
            var admin = arg.Player();
            if (admin != null && !permission.UserHasPermission(admin.UserIDString, perm_admin)) return;

            if (arg.Args == null || arg.Args.Length == 0 || !ulong.TryParse(arg.Args[0], out var userid)) return;            

            if (!Components.TryGetValue(userid, out var component)) return;
            component.runForcedOff = true;
        }

        [ConsoleCommand("msdisableswim")]
        void DisableSwimCMD(ConsoleSystem.Arg arg)
        {
            var admin = arg.Player();
            if (admin != null && !permission.UserHasPermission(admin.UserIDString, perm_admin)) return;

            if (arg.Args == null || arg.Args.Length == 0 || !ulong.TryParse(arg.Args[0], out var userid)) return;

            if (!Components.TryGetValue(userid, out var component)) return;
            component.swimForcedOff = true;
        }

        [ConsoleCommand("msenablerun")]
        void EnableRunCMD(ConsoleSystem.Arg arg)
        {
            var admin = arg.Player();
            if (admin != null && !permission.UserHasPermission(admin.UserIDString, perm_admin)) return;

            if (arg.Args == null || arg.Args.Length == 0 || !ulong.TryParse(arg.Args[0], out var userid)) return;

            if (!Components.TryGetValue(userid, out var component)) return;
            component.runForcedOff = false;
        }

        [ConsoleCommand("msenableswim")]
        void EnableSwimCMD(ConsoleSystem.Arg arg)
        {
            var admin = arg.Player();
            if (admin != null && !permission.UserHasPermission(admin.UserIDString, perm_admin)) return;

            if (arg.Args == null || arg.Args.Length == 0 || !ulong.TryParse(arg.Args[0], out var userid)) return;

            if (!Components.TryGetValue(userid, out var component)) return;
            component.swimForcedOff = false;
        }

        public class RunBehaviour : FacepunchBehaviour
		{
            public BasePlayer player;

            private float NextUpdate;
			public float Interval;
			private bool applied;
            private bool Paused;

			public float RunModifier;
			public float RunVelocity;

			public float SwimModifier;
			public float SwimVelocity;

            public bool runEnabled;
            public bool swimEnabled;

            public bool runForcedOff;
            public bool swimForcedOff;

            public bool allowWounded;

            /// <summary>Set before Destroy on unload so OnDestroy skips antihack reset.</summary>
            public bool SuppressAntiHackReset;

            public void PauseSpeed(bool pause)
            {
                Paused = pause;
            }

			public float GetRunVelocity()
			{
				return RunModifier;
			}

			public void SetRunVelocity(float multiplier)
			{
                RunModifier = multiplier;
                RunVelocity = (float)Math.Pow(1.2f, RunModifier);
            }
            public float GetSwimVelocity()
            {
                return SwimModifier;
            }

            public void SetSwimVelocity(float multiplier)
            {
                SwimModifier = multiplier;
				SwimVelocity = (float)Math.Pow(1.2f, SwimModifier);
            }

            
			void Awake()
			{
                player = GetComponent<BasePlayer>();
				NextUpdate = Time.time + Interval;
				player.PauseSpeedHackDetection(float.MaxValue);
                ClearPlayerAntiHackState(player);
                runEnabled = true;
                swimEnabled = true;
                allowWounded = Instance.config.Allow_Wounded;
            }

			void FixedUpdate()
			{
				if (Time.time < NextUpdate) return;
				NextUpdate = Time.time + Interval;

				if (Paused || !IsPlayerMoving(player, out var direction, out var velocity) || (!allowWounded && player.IsCrawling()) || (!Instance.config.Allow_while_crouching && player.IsDucked()))
				{
					if (applied)
					{
						player.ApplyInheritedVelocity(Vector3.zero);
						applied = false;
					}
                    return;
                }



                applied = true;
				player.ApplyInheritedVelocity(direction * velocity);
                player.AddTempSpeedHackBudget(velocity * 1.25f, 1f);
                player.PauseTickDistanceDetection(1f);
                player.PauseFlyHackDetection(1f);
                player.ApplyStallProtection(1f);


                //1716.ResetAntiHack();
            }

            private bool IsMovingFoward()
			{
                return player.serverInput.IsDown(BUTTON.FORWARD) && (!player.serverInput.IsDown(BUTTON.BACKWARD) &&
               !player.serverInput.IsDown(BUTTON.LEFT) && !player.serverInput.IsDown(BUTTON.RIGHT));
            }

            private bool IsPlayerMoving(BasePlayer player, out Vector3 direction, out float velocity)
            {
				velocity = 0;
				if ((!Instance.config.Always_running && !player.serverInput.IsDown(BUTTON.SPRINT)) || !IsMovingFoward() || player.isMounted || player.IsFlying )
				{
					direction = Vector3.zero;
					return false;
				}
				if (player.IsSwimming())
				{
					if (SwimVelocity <= 0 || !swimEnabled || swimForcedOff)
					{
						direction = Vector3.zero;
						return false;
					}
                    direction = player.eyes.BodyForward();
					velocity = SwimVelocity;
					return true;
                }

				if (RunVelocity <= 0 || !runEnabled || runForcedOff)
				{
					direction = Vector3.zero;
					return false;
				}

				direction = player.eyes.HeadForward();
                direction.y = Mathf.Clamp(direction.y, -0.3f, 0.3f);
				velocity = RunVelocity;

                if (!player.IsOnGround())
                    velocity = Mathf.Min(velocity, 6.2f);

                return true;
            }

            void OnDestroy()
			{
                if (!SuppressAntiHackReset)
                    ClearPlayerAntiHackState(player);
                if (player != null)
                {
                    player.PauseSpeedHackDetection(0.01f);
                    player.ApplyInheritedVelocity(Vector3.zero);
                }
            }
		}

        /// <summary>
        /// Clears antihack buckets without calling BasePlayer.ResetAntiHack (signature changed across updates;
        /// a compile-time call to a removed overload becomes an uncatchable MissingMethodException at JIT).
        /// </summary>
        private static void ClearPlayerAntiHackState(BasePlayer player)
        {
            if (player == null || player.ActivePlayerInd < 0)
                return;

            try
            {
                int ind = player.ActivePlayerInd;
                if (AntiHack.PlayerStates.IsCreated)
                    AntiHack.PlayerStates[ind] = default;
                if (AntiHack.PlayerNoclipStates.IsCreated)
                    AntiHack.PlayerNoclipStates[ind] = default;
                if (AntiHack.PlayerSpeedhackStates.IsCreated)
                    AntiHack.PlayerSpeedhackStates[ind] = default;
                if (AntiHack.PlayerFlyhackStates.IsCreated)
                    AntiHack.PlayerFlyhackStates[ind] = default;
                player.rpcHistory?.Clear();
            }
            catch
            {
            }
        }

        #endregion

        #region API

        List<ulong> PausedByEvent = new List<ulong>();

        void OnEventLeave(BasePlayer player, string eventName) 
        {
            PausedByEvent.Remove(player.userID);
            if (!PausedByEvent.Contains(player.userID))
                PauseSpeedBoost(player.userID, false);
        }

        void OnEventJoined(BasePlayer player, string eventName)
        {
            PausedByEvent.Add(player.userID);
            PauseSpeedBoost(player.userID, true);
        }


        Dictionary<ulong, List<Mods>> PluginMods = new Dictionary<ulong, List<Mods>>();
        public class Mods
        {
            public string plugin;
            public BasePlayer player;

            public float runSpeed;
            public Action r_check;

            public float swimSpeed;
            public Action s_check;

            public void Init(BasePlayer player, string plugin)
            {
                this.plugin = plugin;
                r_check = () => CheckRunTime(player.userID);
                s_check = () => CheckSwimTime(player.userID);
                this.player = player;
            }

            public void SetRunSpeed(float speed, float duration)
            {
                if (speed == 0) return;
                ServerMgr.Instance.CancelInvoke(r_check);
                runSpeed = speed;
                if (duration > 0) ServerMgr.Instance.Invoke(r_check, duration);
                Instance.SetRunspeed(player, runSpeed);
            }

            public void SetSwimSpeed(float speed, float duration)
            {
                if (speed == 0) return;
                ServerMgr.Instance.CancelInvoke(s_check);
                swimSpeed = speed;
                if (duration > 0) ServerMgr.Instance.Invoke(s_check, duration);
                Instance.SetSwimspeed(player, swimSpeed);
            }

            private void CheckRunTime(ulong userid)
            {
                runSpeed = 0;
                Instance.CheckData(userid, this);
            }

            private void CheckSwimTime(ulong userid)
            {
                swimSpeed = 0;
                Instance.CheckData(userid, this);
            }

            public void Destroy()
            {
                ServerMgr.Instance.CancelInvoke(r_check);
                ServerMgr.Instance.CancelInvoke(s_check);
            }
        }

        public void PauseSpeedBoost(ulong id, bool pause)
        {
            if (!Components.TryGetValue(id, out var component)) return;
            component.PauseSpeed(pause);
            Interface.Oxide.LogInfo($"Setting speed pause for {id}: {pause}");
        }

        public void AddRunSpeedBoost(BasePlayer player, string plugin, float mod, float duration, bool force)
        {
            if (!PluginMods.TryGetValue(player.userID, out var mods)) PluginMods.Add(player.userID, mods = new List<Mods>());
            foreach (var _mod in mods)
            {
                if (_mod.plugin != plugin) continue;
                if (_mod.runSpeed < mod || force) _mod.SetRunSpeed(mod, duration);
                return;
            }
            var newMod = new Mods();
            newMod.Init(player, plugin);
            newMod.SetRunSpeed(mod, duration);
            mods.Add(newMod);
        }

        public void RemoveRunSpeed(BasePlayer player, string plugin)
        {
            if (!PluginMods.TryGetValue(player.userID, out var mods)) return;
            for (int i = 0; i < mods.Count; i++)
            {
                var mod = mods[i];
                if (mod.plugin != plugin) continue;
                mod.runSpeed = 0;
                if (mod.swimSpeed == 0)
                {
                    mod.Destroy();
                    mods.RemoveAt(i);
                    HandleSpeedCheck(player.userID);
                    if (mods.Count == 0) PluginMods.Remove(player.userID);
                }
                return;
            }
        }

        public void AddSwimSpeedBoost(BasePlayer player, string plugin, float mod, float duration, bool force)
        {
            if (!PluginMods.TryGetValue(player.userID, out var mods)) PluginMods.Add(player.userID, mods = new List<Mods>());
            foreach (var _mod in mods)
            {
                if (_mod.plugin != plugin) continue;
                if (_mod.swimSpeed < mod || force) _mod.SetSwimSpeed(mod, duration);
                return;
            }
            var newMod = new Mods();
            newMod.Init(player, plugin);
            newMod.SetSwimSpeed(mod, duration);
            mods.Add(newMod);
        }

        public void RemoveSwimSpeed(BasePlayer player, string plugin)
        {
            if (!PluginMods.TryGetValue(player.userID, out var mods)) return;
            for (int i = 0; i < mods.Count; i++)
            {
                var mod = mods[i];
                if (mod.plugin != plugin) continue;
                mod.swimSpeed = 0;
                if (mod.runSpeed == 0)
                {
                    mod.Destroy();
                    mods.RemoveAt(i);
                    HandleSpeedCheck(player.userID);
                    if (mods.Count == 0) PluginMods.Remove(player.userID);
                }
                return;
            }
        }

        public void CheckData(ulong key, Mods instance)
        {
            if (!PluginMods.TryGetValue(key, out var modData)) return;
            foreach (var mod in modData)
            {
                if (mod != instance) continue;
                modData.Remove(mod);
                HandleSpeedCheck(key);
                break;
            }
            if (modData.Count == 0)
            {
                PluginMods.Remove(key);
            }
        }

        void HandleSpeedCheck(ulong key)
        {
            if (!Components.TryGetValue(key, out var behav)) return;
            if (!HasSwimMovementSpeeds(key.ToString(), out var swim, out var run)) 
                RemoveSpeedComponent(key);
            else
            {
                behav.SetSwimVelocity(swim);
                behav.SetRunVelocity(run);
            }
        }

        #endregion

        #region Zone Manager

        [PluginReference]
        private Plugin ZoneManager;

        Dictionary<string, List<ulong>> playersInZones = new Dictionary<string, List<ulong>>();

        void OnEnterZone(string ZoneID, BasePlayer player) 
        {
            if (!config.zoneManagerSettings.pause_zones.Contains(ZoneID)) return;
            if (!playersInZones.TryGetValue(ZoneID, out var zonePlayers)) playersInZones.Add(ZoneID, zonePlayers = new List<ulong>());
            zonePlayers.Add(player.userID);
            PauseSpeedBoost(player.userID, true);
        }

        void OnExitZone(string ZoneID, BasePlayer player) 
        {
            if (!config.zoneManagerSettings.pause_zones.Contains(ZoneID)) return;
            if (!playersInZones.TryGetValue(ZoneID, out var zonePlayers)) return;
            if (!zonePlayers.Contains(player.userID)) return;

            zonePlayers.Remove(player.userID);
            if (zonePlayers.Count == 0) playersInZones.Remove(ZoneID);

            bool stillInNoSpeedZone = false;
            foreach (var zone in playersInZones)
            {
                if (zone.Value.Contains(player.userID))
                {
                    stillInNoSpeedZone = true;
                    break;
                }
            }
            if (!stillInNoSpeedZone) PauseSpeedBoost(player.userID, false);
        }

        #endregion
    }
}
 