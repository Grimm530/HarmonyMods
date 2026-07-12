using System.Collections.Generic;

namespace GrimmNPC2
{
    public partial class GrimmNPC2
    {
        /// <summary>Named <see cref="CustomNpcData2"/> templates (plugin preset registry).</summary>
        private readonly Dictionary<string, CustomNpcData2> _profileTemplates = new Dictionary<string, CustomNpcData2>(64);

        /// <summary>
        /// Registers or replaces a reusable profile. Values are cloned on <see cref="TryGetProfileTemplateClone"/>.
        /// Use <see cref="CustomNpcData2.PresetId"/> or any key your spawn plugin agrees on.
        /// </summary>
        public static void RegisterProfileTemplate(string presetId, CustomNpcData2 template)
        {
            if (Instance == null || string.IsNullOrWhiteSpace(presetId) || template == null) return;
            string key = presetId.Trim();
            Instance._profileTemplates[key] = template.CloneNormalized();
        }

        public static bool UnregisterProfileTemplate(string presetId)
        {
            if (Instance == null || string.IsNullOrWhiteSpace(presetId)) return false;
            return Instance._profileTemplates.Remove(presetId.Trim());
        }

        /// <summary>Returns a fresh normalized clone of the registered template.</summary>
        public static bool TryGetProfileTemplateClone(string presetId, out CustomNpcData2 data)
        {
            data = null;
            if (Instance == null || string.IsNullOrWhiteSpace(presetId)) return false;
            if (!Instance._profileTemplates.TryGetValue(presetId.Trim(), out var t) || t == null) return false;
            data = t.CloneNormalized();
            return true;
        }

        /// <summary>
        /// If <paramref name="presetId"/> is registered, assigns <paramref name="data"/> from the template;
        /// otherwise leaves <paramref name="data"/> unchanged.
        /// </summary>
        public static bool TryApplyProfileTemplateById(string presetId, ref CustomNpcData2 data)
        {
            if (!TryGetProfileTemplateClone(presetId, out var clone)) return false;
            data = clone;
            return true;
        }

        /// <summary>
        /// Detects which scientist GEN2 FSM component is on the entity. Order: Shotgun, Heavy, default
        /// <see cref="Rust.Ai.Gen2.Scientist2FSM"/> (mutually exclusive on one pawn).
        /// </summary>
        public static ScientistGen2FsmKind DetectScientistFsmKind(BaseEntity entity)
        {
            if (entity == null) return ScientistGen2FsmKind.Unknown;
            if (entity.GetComponent<Rust.Ai.Gen2.Scientist2FSM_Shotgun>() != null) return ScientistGen2FsmKind.Shotgun;
            if (entity.GetComponent<Rust.Ai.Gen2.Scientist2FSM_Heavy>() != null) return ScientistGen2FsmKind.Heavy;
            if (entity.GetComponent<Rust.Ai.Gen2.Scientist2FSM>() != null) return ScientistGen2FsmKind.DefaultScientist;
            return ScientistGen2FsmKind.Unknown;
        }

        private void ClearProfileTemplates()
        {
            _profileTemplates.Clear();
        }
    }
}
