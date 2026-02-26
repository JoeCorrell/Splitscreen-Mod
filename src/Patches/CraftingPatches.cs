using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using ValheimSplitscreen.Core;

namespace ValheimSplitscreen.Patches
{
    /// <summary>
    /// Ensures crafting/inventory sub-methods run with the correct player context.
    /// InventoryGuiPatches already swaps m_localPlayer during InventoryGui.Update(),
    /// but some methods called from Update may escape the swap or run at other times.
    /// These patches add safety swaps + logging.
    /// Uses a stack to handle nested calls (e.g. UpdateCraftingPanel calling DoCrafting).
    /// </summary>
    [HarmonyPatch]
    public static class CraftingPatches
    {
        private static readonly Stack<global::Player> _savedLocalStack = new Stack<global::Player>();

        private static global::Player GetOwnerIfP2()
        {
            if (!SplitScreenManager.Instance?.SplitscreenActive ?? true) return null;
            if (InventoryGuiPatches.ActiveOwnerPlayerIndex != 1) return null;
            return SplitScreenManager.Instance.PlayerManager?.Player2;
        }

        private static bool SwapIfNeeded()
        {
            var p2 = GetOwnerIfP2();
            if (p2 == null) return false;
            if (global::Player.m_localPlayer == p2) return false;

            _savedLocalStack.Push(global::Player.m_localPlayer);
            global::Player.m_localPlayer = p2;
            return true;
        }

        private static void RestoreIfSwapped(bool wasSwapped)
        {
            if (wasSwapped && _savedLocalStack.Count > 0)
            {
                global::Player.m_localPlayer = _savedLocalStack.Pop();
            }
        }

        private static string LocalName() => global::Player.m_localPlayer?.GetPlayerName() ?? "null";

        [HarmonyPatch(typeof(InventoryGui), "UpdateCraftingPanel")]
        [HarmonyPrefix]
        public static void UpdateCraftingPanel_Prefix(out bool __state)
        {
            __state = SwapIfNeeded();
            if (SplitscreenLog.ShouldLog("Crafting.panel", 10f))
                SplitscreenLog.Log("Crafting", $"UpdateCraftingPanel: owner=P{InventoryGuiPatches.ActiveOwnerPlayerIndex + 1}, m_localPlayer='{LocalName()}'");
        }

        [HarmonyPatch(typeof(InventoryGui), "UpdateCraftingPanel")]
        [HarmonyPostfix]
        public static void UpdateCraftingPanel_Postfix(bool __state) => RestoreIfSwapped(__state);

        [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
        [HarmonyPrefix]
        public static void DoCrafting_Prefix(out bool __state)
        {
            __state = SwapIfNeeded();
            SplitscreenLog.Log("Crafting", $"DoCrafting: owner=P{InventoryGuiPatches.ActiveOwnerPlayerIndex + 1}, m_localPlayer='{LocalName()}'");
        }

        [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
        [HarmonyPostfix]
        public static void DoCrafting_Postfix(bool __state) => RestoreIfSwapped(__state);

        [HarmonyPatch(typeof(InventoryGui), "UpdateRecipeList")]
        [HarmonyPrefix]
        public static void UpdateRecipeList_Prefix(out bool __state)
        {
            __state = SwapIfNeeded();
            if (SplitscreenLog.ShouldLog("Crafting.recipes", 10f))
                SplitscreenLog.Log("Crafting", $"UpdateRecipeList: owner=P{InventoryGuiPatches.ActiveOwnerPlayerIndex + 1}, m_localPlayer='{LocalName()}'");
        }

        [HarmonyPatch(typeof(InventoryGui), "UpdateRecipeList")]
        [HarmonyPostfix]
        public static void UpdateRecipeList_Postfix(bool __state) => RestoreIfSwapped(__state);

        [HarmonyPatch(typeof(InventoryGui), "OnCraftPressed")]
        [HarmonyPrefix]
        public static void OnCraftPressed_Prefix(out bool __state)
        {
            __state = SwapIfNeeded();
            SplitscreenLog.Log("Crafting", $"OnCraftPressed: owner=P{InventoryGuiPatches.ActiveOwnerPlayerIndex + 1}, m_localPlayer='{LocalName()}'");
        }

        [HarmonyPatch(typeof(InventoryGui), "OnCraftPressed")]
        [HarmonyPostfix]
        public static void OnCraftPressed_Postfix(bool __state) => RestoreIfSwapped(__state);

        [HarmonyPatch(typeof(InventoryGui), "OnSelectedItem")]
        [HarmonyPrefix]
        public static void OnSelectedItem_Prefix(out bool __state)
        {
            __state = SwapIfNeeded();
            if (SplitscreenLog.ShouldLog("Crafting.select", 2f))
                SplitscreenLog.Log("Crafting", $"OnSelectedItem: owner=P{InventoryGuiPatches.ActiveOwnerPlayerIndex + 1}, m_localPlayer='{LocalName()}'");
        }

        [HarmonyPatch(typeof(InventoryGui), "OnSelectedItem")]
        [HarmonyPostfix]
        public static void OnSelectedItem_Postfix(bool __state) => RestoreIfSwapped(__state);

        [HarmonyPatch(typeof(InventoryGui), "RepairOneItem")]
        [HarmonyPrefix]
        public static void RepairOneItem_Prefix(out bool __state)
        {
            __state = SwapIfNeeded();
            SplitscreenLog.Log("Crafting", $"RepairOneItem: owner=P{InventoryGuiPatches.ActiveOwnerPlayerIndex + 1}");
        }

        [HarmonyPatch(typeof(InventoryGui), "RepairOneItem")]
        [HarmonyPostfix]
        public static void RepairOneItem_Postfix(bool __state) => RestoreIfSwapped(__state);
    }
}
