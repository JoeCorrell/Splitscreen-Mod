using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimSplitscreen.Core
{
    /// <summary>
    /// Centralized logging with player context and reusable m_localPlayer swap helpers.
    /// All log lines are tagged [SS][P1] or [SS][P2] based on the current m_localPlayer.
    /// </summary>
    public static class SplitscreenLog
    {
        private static readonly Dictionary<string, float> _lastLogTimes = new Dictionary<string, float>();

        /// <summary>
        /// Returns 1 if m_localPlayer is P1 (or no splitscreen), 2 if m_localPlayer is P2.
        /// </summary>
        public static int CurrentPlayerIndex
        {
            get
            {
                var mgr = SplitScreenManager.Instance?.PlayerManager;
                if (mgr == null) return 1;
                if (mgr.IsPlayer2(global::Player.m_localPlayer)) return 2;
                return 1;
            }
        }

        public static string Tag => $"[SS][P{CurrentPlayerIndex}]";

        public static void Log(string system, string msg)
        {
            Debug.Log($"[SS][P{CurrentPlayerIndex}][{system}] {msg}");
        }

        public static void Warn(string system, string msg)
        {
            Debug.LogWarning($"[SS][P{CurrentPlayerIndex}][{system}] {msg}");
        }

        public static void Err(string system, string msg)
        {
            Debug.LogError($"[SS][P{CurrentPlayerIndex}][{system}] {msg}");
        }

        /// <summary>
        /// Rate-limited logging. Returns true if enough time has elapsed since the last log with this key.
        /// Usage: if (SplitscreenLog.ShouldLog("Hotbar.update", 5f)) SplitscreenLog.Log("Hotbar", "...");
        /// </summary>
        public static bool ShouldLog(string key, float intervalSec = 5f)
        {
            float now = Time.time;
            if (_lastLogTimes.TryGetValue(key, out float lastTime) && now - lastTime < intervalSec)
                return false;
            _lastLogTimes[key] = now;
            return true;
        }

        /// <summary>
        /// Temporarily swap m_localPlayer to the given player, execute the action, then restore.
        /// </summary>
        public static void ExecuteAsPlayer(global::Player player, Action action)
        {
            if (player == null)
            {
                action?.Invoke();
                return;
            }

            var prev = global::Player.m_localPlayer;
            try
            {
                global::Player.m_localPlayer = player;
                action?.Invoke();
            }
            finally
            {
                global::Player.m_localPlayer = prev;
            }
        }

        /// <summary>
        /// Temporarily swap m_localPlayer to the given player, execute the function, then restore.
        /// </summary>
        public static T ExecuteAsPlayer<T>(global::Player player, Func<T> func)
        {
            if (player == null)
                return func != null ? func() : default;

            var prev = global::Player.m_localPlayer;
            try
            {
                global::Player.m_localPlayer = player;
                return func != null ? func() : default;
            }
            finally
            {
                global::Player.m_localPlayer = prev;
            }
        }

        /// <summary>
        /// Get the Player object for a given player index (0=P1, 1=P2).
        /// </summary>
        public static global::Player GetPlayer(int index)
        {
            if (index == 1)
                return SplitScreenManager.Instance?.PlayerManager?.Player2;
            return global::Player.m_localPlayer;
        }
    }
}
