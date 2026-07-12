using UnityEngine;

namespace Backpacks
{
    /// <summary>
    /// Attached to our virtual backpack StorageContainer so we can identify and save it when loot closes.
    /// </summary>
    public class BackpackStorageMarker : MonoBehaviour
    {
        public ulong OwnerId { get; set; }
        public int PageIndex { get; set; }
    }
}
