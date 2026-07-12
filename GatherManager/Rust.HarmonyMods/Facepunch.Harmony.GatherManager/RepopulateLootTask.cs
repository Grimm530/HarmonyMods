using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace Facepunch.Harmony.GatherManager
{
    public class RepopulateLootTask
    {
        public bool Finished { get; private set; }

        public int EntityIndex { get; private set; }

        public int EntityCount { get; private set; }

        public RepopulateLootTask()
        {

        }

        public void Start()
        {
            // Use ServerMgr since we arent oxide & needing to hotload
            // Only refreshes loot, its rare we need to do this anyways
            ServerMgr.Instance.StartCoroutine( Coroutine() );
        }

        private IEnumerator Coroutine()
        {
            var entities = new List<LootContainer>();
            foreach (var entity in BaseNetworkable.serverEntities)
            {
                if (entity is LootContainer lc)
                    entities.Add(lc);
            }

            EntityCount = entities.Count;

            yield return null;

            Stopwatch watch = Stopwatch.StartNew();

            for (int idx = 0; idx < entities.Count; idx++)
            {
                var entity = entities[idx];
                if ( watch.ElapsedMilliseconds > 20 )
                {
                    yield return null;
                    watch.Restart();
                }

                try
                {
                    EntityIndex++;

                    if ( entity == null || entity.IsDestroyed )
                    {
                        continue;
                    }

                    entity.SpawnLoot();
                }
                catch ( Exception ex )
                {
                    UnityEngine.Debug.LogException( ex );
                }
            }

            Finished = true;
        }
    }
}
