namespace ValheimSplitscreen.Patches
{
    /// <summary>
    /// Menu/UI notes for splitscreen:
    ///
    /// - Menus overlay the full screen (shared UI element)
    /// - Pausing is already prevented by GamePatches.CanPause/IsPaused
    /// - Chat works through the network layer via ZDO/RPC, Player 2's name appears naturally
    /// - Player 2's messages are shown through the IMGUI overlay (SplitHudManager)
    ///
    /// No Harmony patches needed - the existing game UI works correctly with our approach.
    /// </summary>
    public static class MenuPatches
    {
    }
}
