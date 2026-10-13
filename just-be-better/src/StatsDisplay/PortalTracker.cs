// Ported from LookingGlass 1.16.1 by DropPod and Wet-Boys, github.com/Wet-Boys/LookingGlass
// LookingGlass is under the GNU LGPL v3, and so is this file, see LICENSE and COPYING
// Changes for Just_Be_Better: flags instead of fields, MMHOOK events instead of detours, spawners read when
// asked rather than from their change hook, marks kept per stage scene rather than reset on a stage hook, the
// artifact portal from the server opening it, its comments dropped

using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using RoR2;
using UnityEngine;

namespace JustBeBetter.StatsDisplay
{
    // Which portals the teleporter will open this stage, from the teleporter's own flags, the portal spawners'
    // synced flags, and the artifact code and access nodes once used
    internal sealed class PortalTracker
    {
        private readonly ManualLogSource log;

        // A spawner rolls in Start on the host before stage hooks run, and a client gets no change hook for a
        // roll made before it joined, so the synced flag is read when asked
        private readonly List<PortalSpawner> spawners = new List<PortalSpawner>();

        private FieldInfo? previewField;
        private StatMath.Portals marked;
        private int markedScene;
        private bool failed;

        internal PortalTracker(ManualLogSource log)
        {
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        internal void Enable()
        {
            previewField = typeof(PortalSpawner).GetField("previewChildName", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (previewField == null)
            {
                log.LogWarning("Portal spawners have changed, so void, storm, gold and encrypted portals from them aren't tracked");
            }

            On.RoR2.PortalSpawner.Start += OnSpawnerStart;
            On.RoR2.PortalDialerController.OpenArtifactPortalServer += OnArtifactPortal;
            On.EntityStates.Missions.AccessCodes.Node.NodesOnAndReady.OnEnter += OnNodesReady;
        }

        internal void Disable()
        {
            On.RoR2.PortalSpawner.Start -= OnSpawnerStart;
            On.RoR2.PortalDialerController.OpenArtifactPortalServer -= OnArtifactPortal;
            On.EntityStates.Missions.AccessCodes.Node.NodesOnAndReady.OnEnter -= OnNodesReady;
            spawners.Clear();
        }

        // Null off a teleporter stage
        internal StatMath.Portals? Current()
        {
            TeleporterInteraction teleporter = TeleporterInteraction.instance;
            if (!teleporter)
            {
                return null;
            }

            int scene = teleporter.gameObject.scene.handle;
            StatMath.Portals portals = markedScene == scene ? marked : StatMath.Portals.None;
            if (teleporter.shouldAttemptToSpawnShopPortal)
            {
                portals |= StatMath.Portals.Bazaar;
            }

            if (teleporter.shouldAttemptToSpawnGoldshoresPortal)
            {
                portals |= StatMath.Portals.Gold;
            }

            if (teleporter.shouldAttemptToSpawnMSPortal)
            {
                portals |= StatMath.Portals.Celestial;
            }

            spawners.RemoveAll(s => !s);
            if (!failed && previewField != null)
            {
                try
                {
                    foreach (PortalSpawner spawner in spawners)
                    {
                        if (spawner.WillSpawn && spawner.gameObject.scene.handle == scene)
                        {
                            portals |= PortalOf(previewField.GetValue(spawner) as string);
                        }
                    }
                }
                catch (Exception e)
                {
                    failed = true;
                    log.LogError($"Couldn't read a portal spawner, so portals from them aren't tracked from here: {e}");
                }
            }

            return portals;
        }

        private void OnSpawnerStart(On.RoR2.PortalSpawner.orig_Start orig, PortalSpawner self)
        {
            orig(self);
            spawners.RemoveAll(s => !s);
            if (!spawners.Contains(self))
            {
                spawners.Add(self);
            }
        }

        // Only a code that opens one, and so only for the host or in solo, as LookingGlass's private state hook
        // also counted wrong codes
        private void OnArtifactPortal(On.RoR2.PortalDialerController.orig_OpenArtifactPortalServer orig, PortalDialerController self, ArtifactDef artifact)
        {
            orig(self, artifact);
            Mark(self, StatMath.Portals.Artifact);
        }

        private void OnNodesReady(On.EntityStates.Missions.AccessCodes.Node.NodesOnAndReady.orig_OnEnter orig, EntityStates.Missions.AccessCodes.Node.NodesOnAndReady self)
        {
            orig(self);
            if (self.outer)
            {
                Mark(self.outer, StatMath.Portals.Node);
            }
        }

        // Kept with the scene it happened in, so the next stage starts clear without a stage hook's timing
        private void Mark(Component source, StatMath.Portals portal)
        {
            int scene = source.gameObject.scene.handle;
            if (scene != markedScene)
            {
                marked = StatMath.Portals.None;
                markedScene = scene;
            }

            marked |= portal;
        }

        private static StatMath.Portals PortalOf(string? preview)
        {
            switch (preview)
            {
                case "VoidPortalIndicator":
                    return StatMath.Portals.Void;
                case "StormPortalIndicator":
                    return StatMath.Portals.Storm;
                case "AccessCodesSymbol":
                case "CEPortalIndicator":
                    return StatMath.Portals.Encrypted;
                case "GoldshoresPortalIndicator":
                    return StatMath.Portals.Gold;
                default:
                    return StatMath.Portals.None;
            }
        }
    }
}
