using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NoVFXOnTrees
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    internal class NoVFXOnTrees : BaseUnityPlugin
    {
        public const string PluginGUID = "ewigl.NoVFXOnTrees";
        public const string PluginName = "No VFX On Trees";
        public const string PluginVersion = "0.9.2";

        private readonly Harmony _harmony = new("ewigl.NoVFXOnTrees");

        private static ConfigEntry<bool> _disableDestructionSmoke;
        private static ConfigEntry<bool> _disableHitSawdust;
        private static ConfigEntry<bool> _disableHitSmoke;

        private static readonly HashSet<EffectList.EffectData[]> _trackedDestroyedEffectLists = new();
        private static readonly HashSet<EffectList.EffectData[]> _trackedHitEffectLists = new();

        private static readonly Dictionary<GameObject, bool> _originalNodeStates = new();

        public void Awake()
        {
            _disableDestructionSmoke = Config.Bind(
                "Destruction",
                "DisableSmoke",
                true,
                "Remove smoke effects when trees are destroyed.");

            _disableHitSawdust = Config.Bind(
                "Hit",
                "DisableSawdust",
                true,
                "Remove sawdust effects when trees are hit. (Will also remove smoke effects cause it's part of the same VFX.)");

            _disableHitSmoke = Config.Bind(
                "Hit",
                "DisableSmoke",
                true,
                "Remove smoke effects when trees are hit.");


            _disableDestructionSmoke.SettingChanged += OnConfigChanged;
            _disableHitSawdust.SettingChanged += OnConfigChanged;
            _disableHitSmoke.SettingChanged += OnConfigChanged;

            _harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        private void OnConfigChanged(object sender, EventArgs e)
        {
            ApplyTrackedEffects();
        }

        internal static void TrackDestroyedEffects(EffectList.EffectData[] effects)
        {
            if (effects == null) return;
            _trackedDestroyedEffectLists.Add(effects);
        }

        internal static void TrackHitEffects(EffectList.EffectData[] effects)
        {
            if (effects == null) return;
            _trackedHitEffectLists.Add(effects);
        }

        internal static void ApplyTrackedEffects()
        {
            var nodesToDisable = new HashSet<GameObject>();

            CollectDestructionEffects(_trackedDestroyedEffectLists, _disableDestructionSmoke.Value, nodesToDisable);

            CollectHitEffects(_trackedHitEffectLists, _disableHitSawdust.Value, _disableHitSmoke.Value, nodesToDisable);

            foreach (var nodeState in _originalNodeStates)
            {
                if (nodeState.Key != null)
                {
                    bool shouldDisable = nodesToDisable.Contains(nodeState.Key);
                    nodeState.Key.SetActive(!shouldDisable && nodeState.Value);
                }
            }
        }

        private static void CollectDestructionEffects(
            HashSet<EffectList.EffectData[]> effectLists,
            bool disableSmoke,
            HashSet<GameObject> nodesToDisable)
        {
            if (!disableSmoke) return;

            foreach (var effects in effectLists)
            {
                foreach (var effect in effects)
                {
                    if (effect?.m_prefab == null) continue;

                    foreach (Transform child in effect.m_prefab.transform)
                    {
                        if (child.name.IndexOf("smokepoff", StringComparison.Ordinal) >= 0)
                        {
                            RecordAndCollectNode(child.gameObject, nodesToDisable);
                        }
                    }
                }
            }
        }

        private static void CollectHitEffects(
            HashSet<EffectList.EffectData[]> effectLists,
            bool disableSawdust,
            bool disableSmoke,
            HashSet<GameObject> nodesToDisable)
        {
            foreach (var effects in effectLists)
            {
                foreach (var effect in effects)
                {
                    if (effect?.m_prefab == null) continue;

                    if (disableSawdust &&
                        effect.m_prefab.name.IndexOf("SawDust", StringComparison.Ordinal) >= 0)
                    {
                        RecordAndCollectNode(effect.m_prefab, nodesToDisable);
                        continue;
                    }

                    foreach (Transform child in effect.m_prefab.transform)
                    {
                        if (disableSawdust &&
                            child.name.IndexOf("SawDust", StringComparison.Ordinal) >= 0)
                        {
                            RecordAndCollectNode(child.gameObject, nodesToDisable);
                        }

                        if (disableSmoke &&
                            child.name.IndexOf("smokepoff", StringComparison.Ordinal) >= 0)
                        {
                            RecordAndCollectNode(child.gameObject, nodesToDisable);
                        }
                    }
                }
            }
        }

        private static void RecordAndCollectNode(GameObject go, HashSet<GameObject> nodesToDisable)
        {
            if (!_originalNodeStates.ContainsKey(go))
            {
                _originalNodeStates.Add(go, go.activeSelf);
            }
            nodesToDisable.Add(go);
        }
    }

    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ZNetSceneAwakePatch
    {
        private static void Postfix(ZNetScene __instance)
        {
            foreach (GameObject prefab in __instance.m_prefabs)
            {
                if (prefab.TryGetComponent<TreeBase>(out var treeBase))
                {
                    NoVFXOnTrees.TrackDestroyedEffects(treeBase.m_destroyedEffect.m_effectPrefabs);
                    NoVFXOnTrees.TrackHitEffects(treeBase.m_hitEffect.m_effectPrefabs);
                }
                else if (prefab.TryGetComponent<TreeLog>(out var treeLog))
                {
                    NoVFXOnTrees.TrackDestroyedEffects(treeLog.m_destroyedEffect.m_effectPrefabs);
                    NoVFXOnTrees.TrackHitEffects(treeLog.m_hitEffect.m_effectPrefabs);
                }
                else if (prefab.TryGetComponent<Destructible>(out var destructible) && destructible.m_destructibleType == DestructibleType.Tree)
                {
                    NoVFXOnTrees.TrackDestroyedEffects(destructible.m_destroyedEffect.m_effectPrefabs);
                    NoVFXOnTrees.TrackHitEffects(destructible.m_hitEffect.m_effectPrefabs);
                }
            }

            NoVFXOnTrees.ApplyTrackedEffects();
        }
    }
}