using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace IndustrialTransferSpeed
{
    public static class PlanterProductionUi
    {
        private const string PanelName = "IndustrialTransferSpeed.PlanterProduction";
        private const string CommandPrefix = "cui.endtest ITS_PLANTER_MODE ";

        public static void Show(BasePlayer player, PlanterBox planter)
        {
            if (player == null || planter == null || planter.IsDestroyed)
            {
                return;
            }

            Destroy(player);

            string currentMode = PlanterProductionSettings.GetMode(planter);
            string currentText = PlanterProductionSettings.GetDisplayName(currentMode);

            List<JObject> elements = new List<JObject>
            {
                IndustrialCuiHelper.Panel(PanelName, "OverlayNonScaled", "0.05 0.06 0.07 0.88", "0.5 0.5", "0.5 0.5", "120 120", "522 200", needsCursor: true),
                IndustrialCuiHelper.Label(PanelName + ".Title", PanelName, "Industrial Planter Output", 13, "0.03 0.64", "0.72 0.96", "1 0.78 0.38 1", "MiddleLeft"),
                IndustrialCuiHelper.Label(PanelName + ".Current", PanelName, "Current: " + currentText, 11, "0.03 0.39", "0.72 0.67", "1 1 1 0.75", "MiddleLeft")
            };

            elements.AddRange(ModeButton("Harvest", PlanterProductionSettings.ModeFruit, currentMode, "0.03 0.07", "0.29 0.35"));
            elements.AddRange(ModeButton("Seeds", PlanterProductionSettings.ModeSeed, currentMode, "0.32 0.07", "0.58 0.35"));
            elements.AddRange(ModeButton("Clones", PlanterProductionSettings.ModeClone, currentMode, "0.61 0.07", "0.87 0.35"));
            elements.AddRange(IndustrialCuiHelper.Button(PanelName + ".Close", PanelName, "0.65 0.16 0.16 0.75", "X", 12, "0.91 0.64", "0.98 0.94", "cui.endtest ITS_PLANTER_CLOSE"));

            IndustrialCuiHelper.AddUi(player, elements);
        }

        public static void Destroy(BasePlayer player)
        {
            IndustrialCuiHelper.DestroyUi(player, PanelName);
        }

        private static List<JObject> ModeButton(string text, string mode, string currentMode, string anchorMin, string anchorMax)
        {
            bool selected = string.Equals(PlanterProductionSettings.NormalizeMode(currentMode), mode, System.StringComparison.OrdinalIgnoreCase);
            string color = selected ? "0.2 0.65 0.25 0.9" : "0.35 0.35 0.35 0.8";
            return IndustrialCuiHelper.Button(PanelName + "." + mode, PanelName, color, text, 10, anchorMin, anchorMax, CommandPrefix + mode);
        }
    }
}
