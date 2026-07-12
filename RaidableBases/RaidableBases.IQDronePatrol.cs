using Facepunch;
using Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rust;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using static RaidableBases.RaidableBasesExtensionMethods.ExtensionMethods;

namespace RaidableBases
{
    public partial class RaidableBases
    {

        #region IQDronePatrol

        private class CustomPatrol
        {
            public string pluginName;
            public Vector3 position;
            public PositionSetting settingPosition = new();
            public DroneSetting settingDrone = new();

            internal class DroneSetting
            {
                public int droneCountSpawned;
                public int droneAttackedCount;
                public Dictionary<string, int> keyDrones = new();
            }

            internal class PositionSetting
            {
                public int countSpawnPoint;
                public int radiusFindedPoints;
            }
        }

        #endregion

    }
}
