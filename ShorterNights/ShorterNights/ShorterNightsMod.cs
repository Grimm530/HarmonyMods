/*
 * ShorterNights Harmony Mod
 * Speeds up time during night (sunset-sunrise) so nights are ~1/3 normal length.
 * Subscribes to TOD_Time OnSunrise/OnSunset and sets DayLengthInMinutes accordingly.
 * Also shows game time under the hotbar.
 */

using UnityEngine;

namespace ShorterNights
{
    public class ShorterNightsMod : IHarmonyModHooks
    {
        public static ShorterNightsMod Instance { get; private set; }

        /// <summary>Real minutes for a full day cycle. Vanilla typically uses 30.</summary>
        public const float DayLengthMinutes = 30f;
        /// <summary>Real minutes for night - 10 = nights run at 1/3 length (30/3).</summary>
        public const float NightLengthMinutes = 10f;

        private TOD_Time _timeComponent;
        private GameObject _uiRunner;

        public void OnLoaded(OnHarmonyModLoadedArgs args)
        {
            Instance = this;
            ShorterNightsConfig.Load();
            bool showTime = ShorterNightsConfig.Config?.ShowTimeOfDayDisplay == true;

            if (showTime)
            {
                _uiRunner = new GameObject("ShorterNights_TimeDisplay");
                Object.DontDestroyOnLoad(_uiRunner);
                _uiRunner.AddComponent<GameTimeDisplayBehaviour>();
            }

            if (TOD_Sky.Instance == null)
            {
                UnityEngine.Debug.LogWarning("[ShorterNights] TOD_Sky not ready - night speed inactive (load after map). Time display will appear when map loads.");
                UnityEngine.Debug.Log("[ShorterNights] Loaded" + (showTime ? " - game time shown under hotbar." : "."));
                return;
            }
            _timeComponent = TOD_Sky.Instance.Components.Time;
            if (_timeComponent == null)
            {
                UnityEngine.Debug.LogWarning("[ShorterNights] TOD_Time not found - night speed inactive.");
                UnityEngine.Debug.Log("[ShorterNights] Loaded" + (showTime ? " - game time shown under hotbar." : "."));
                return;
            }
            _timeComponent.OnSunrise += OnSunrise;
            _timeComponent.OnSunset += OnSunset;
            UpdateForCurrentTime();
            UnityEngine.Debug.Log("[ShorterNights] Loaded - nights run 3x faster" + (showTime ? ", game time under hotbar." : "."));
        }

        public void OnUnloaded(OnHarmonyModUnloadedArgs args)
        {
            if (_uiRunner != null)
            {
                Object.Destroy(_uiRunner);
                _uiRunner = null;
            }
            GameTimeDisplayUI.DestroyAll();
            if (_timeComponent != null)
            {
                _timeComponent.OnSunrise -= OnSunrise;
                _timeComponent.OnSunset -= OnSunset;
                _timeComponent = null;
            }
            Instance = null;
            UnityEngine.Debug.Log("[ShorterNights] Harmony mod unloaded.");
        }

        private void OnSunrise()
        {
            if (_timeComponent == null || TOD_Sky.Instance == null) return;
            float daySpan = TOD_Sky.Instance.SunsetTime - TOD_Sky.Instance.SunriseTime;
            _timeComponent.DayLengthInMinutes = DayLengthMinutes * (24f / daySpan);
        }

        private void OnSunset()
        {
            if (_timeComponent == null || TOD_Sky.Instance == null) return;
            float daySpan = TOD_Sky.Instance.SunsetTime - TOD_Sky.Instance.SunriseTime;
            _timeComponent.DayLengthInMinutes = NightLengthMinutes * (24f / (24f - daySpan));
        }

        private void UpdateForCurrentTime()
        {
            if (TOD_Sky.Instance == null || _timeComponent == null) return;
            float hour = TOD_Sky.Instance.Cycle.Hour;
            if (hour >= TOD_Sky.Instance.SunsetTime || hour < TOD_Sky.Instance.SunriseTime)
                OnSunset();
            else
                OnSunrise();
        }
    }
}
