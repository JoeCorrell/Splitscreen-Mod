namespace ValheimSplitscreen.Patches
{
    /// <summary>
    /// Network notes for splitscreen:
    ///
    /// Player 2 exists in the ZDO system but is owned by the same machine as Player 1.
    /// - ZDOMan properly manages Player 2's ZDO via ownership (SetOwner)
    /// - ZNet doesn't reject Player 2 because they share Player 1's peer
    /// - RPCs to Player 2's ZDOID are received automatically since same peer
    /// - Zone loading works because Player 2's ZDO position triggers zone loading
    ///
    /// No Harmony patches are needed for networking - it works correctly by default
    /// because Player 2 is just another ZDO owned by the same local peer.
    /// </summary>
    public static class NetworkPatches
    {
    }
}
