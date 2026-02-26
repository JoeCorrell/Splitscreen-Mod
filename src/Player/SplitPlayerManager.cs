using UnityEngine;
using ValheimSplitscreen.Core;
using ValheimSplitscreen.Input;

namespace ValheimSplitscreen.Player
{
    /// <summary>
    /// Manages the second player character lifecycle.
    /// Spawns Player 2 near Player 1 and handles their unique input routing,
    /// profile, and network identity.
    /// </summary>
    public class SplitPlayerManager : MonoBehaviour
    {
        public static SplitPlayerManager Instance { get; private set; }

        /// <summary>The second player's Player component.</summary>
        public global::Player Player2 { get; private set; }

        /// <summary>The second player's PlayerController component.</summary>
        public PlayerController Player2Controller { get; private set; }

        /// <summary>Player 2's separate profile for save data.</summary>
        public PlayerProfile Player2Profile { get; private set; }

        /// <summary>
        /// True when we're in the middle of updating Player 2's controls.
        /// Patches use this to route input correctly.
        /// </summary>
        public bool IsUpdatingPlayer2 { get; set; }

        /// <summary>
        /// Quick check: is the given Player object the second player?
        /// </summary>
        public bool IsPlayer2(global::Player p) => p != null && Player2 != null && p == Player2;

        /// <summary>
        /// Get the player index (0 or 1) for a given Player object.
        /// </summary>
        public int GetPlayerIndex(global::Player p)
        {
            if (p == Player2) return 1;
            return 0;
        }

        private void Awake()
        {
            Instance = this;
        }

        /// <summary>
        /// True during Player 2 instantiation - prevents SetLocalPlayer prefix
        /// from failing to identify the new player as Player 2 (since the field
        /// isn't set until after Instantiate returns).
        /// </summary>
        public bool IsSpawningPlayer2 { get; private set; }

        // Rate-limit logging for per-frame updates
        private float _lastControlsLogTime;
        private float _lastP2AliveCheckTime;

        /// <summary>
        /// Spawn the second player character.
        /// Pass a selected profile, or null to create a new "splitscreen_p2" profile.
        /// </summary>
        public void SpawnSecondPlayer(PlayerProfile selectedProfile = null)
        {
            Debug.Log($"[Splitscreen][Player] SpawnSecondPlayer called, selectedProfile={(selectedProfile != null ? selectedProfile.GetName() : "null")}");

            if (Player2 != null)
            {
                Debug.LogWarning("[Splitscreen][Player] Player 2 already exists! Aborting spawn.");
                return;
            }

            if (global::Player.m_localPlayer == null)
            {
                Debug.LogWarning("[Splitscreen][Player] Can't spawn Player 2: m_localPlayer is null!");
                return;
            }

            if (Game.instance == null)
            {
                Debug.LogWarning("[Splitscreen][Player] Can't spawn Player 2: Game.instance is null!");
                return;
            }

            Debug.Log($"[Splitscreen][Player] m_playerPrefab: {(Game.instance.m_playerPrefab != null ? Game.instance.m_playerPrefab.name : "NULL")}");

            // Use selected profile, or create a new one
            if (selectedProfile != null)
            {
                Player2Profile = selectedProfile;
                Debug.Log($"[Splitscreen][Player] Using existing profile: '{selectedProfile.GetName()}' file='{selectedProfile.GetFilename()}'");
            }
            else
            {
                string p2Name = SplitscreenPlugin.Instance.SplitConfig.Player2Name.Value;
                Player2Profile = new PlayerProfile("splitscreen_p2", FileHelpers.FileSource.Local);
                Player2Profile.SetName(p2Name);
                Player2Profile.Load();
                Debug.Log($"[Splitscreen][Player] Created new profile: '{p2Name}'");
            }

            // Determine spawn position
            Vector3 spawnPos = GetPlayer2SpawnPosition();

            // Set flag BEFORE Instantiate so the SetLocalPlayer prefix can block
            Debug.Log($"[Splitscreen][Player] Setting IsSpawningPlayer2=true, about to Instantiate at {spawnPos}");
            var lpBefore = global::Player.m_localPlayer;
            Debug.Log($"[Splitscreen][Player] m_localPlayer BEFORE instantiate: {lpBefore?.GetPlayerName()} (hash={lpBefore?.GetHashCode()})");
            IsSpawningPlayer2 = true;

            // Instantiate a new player from the same prefab
            GameObject p2Obj = Object.Instantiate(Game.instance.m_playerPrefab, spawnPos, Quaternion.identity);
            Player2 = p2Obj.GetComponent<global::Player>();
            Player2Controller = p2Obj.GetComponent<PlayerController>();
            IsSpawningPlayer2 = false;

            var lpAfter = global::Player.m_localPlayer;
            Debug.Log($"[Splitscreen][Player] m_localPlayer AFTER instantiate: {lpAfter?.GetPlayerName()} (hash={lpAfter?.GetHashCode()})");
            Debug.Log($"[Splitscreen][Player] Player2 component: {(Player2 != null ? "OK" : "NULL")}, PlayerController: {(Player2Controller != null ? "OK" : "NULL")}");

            if (Player2 == null)
            {
                Debug.LogError("[Splitscreen][Player] FAILED to get Player component on spawned Player 2! Destroying object.");
                Destroy(p2Obj);
                return;
            }

            // Set up network view - claim ownership
            var nview = p2Obj.GetComponent<ZNetView>();
            if (nview != null && nview.GetZDO() != null)
            {
                nview.GetZDO().SetOwner(ZDOMan.GetSessionID());
                Debug.Log($"[Splitscreen][Player] Set P2 ZDO owner to session ID {ZDOMan.GetSessionID()}");
            }
            else
            {
                Debug.LogWarning($"[Splitscreen][Player] P2 ZNetView or ZDO is null! nview={nview != null}, zdo={nview?.GetZDO() != null}");
            }

            // Load player data from their profile (inventory, stats, etc.)
            Debug.Log("[Splitscreen][Player] Loading player data from profile...");
            Player2Profile.LoadPlayerData(Player2);

            // Mark as spawned
            Debug.Log("[Splitscreen][Player] Calling Player2.OnSpawned(false)");
            Player2.OnSpawned(false);

            Debug.Log($"[Splitscreen][Player] SUCCESS: Player 2 '{Player2Profile.GetName()}' spawned at {spawnPos}, playerID={Player2.GetPlayerID()}, health={Player2.GetHealth()}/{Player2.GetMaxHealth()}");
        }

        /// <summary>
        /// Determine where Player 2 should spawn based on their saved profile data.
        /// </summary>
        private Vector3 GetPlayer2SpawnPosition()
        {
            var p1 = global::Player.m_localPlayer;

            // Try logout point (where Player 2 last was in this world)
            if (Player2Profile.HaveLogoutPoint())
            {
                Vector3 logoutPoint = Player2Profile.GetLogoutPoint();
                Debug.Log($"[Splitscreen][Player] Spawn position: saved logout point {logoutPoint}");
                Player2Profile.ClearLoguoutPoint();
                return logoutPoint;
            }

            // Try custom spawn point (bed)
            if (Player2Profile.HaveCustomSpawnPoint())
            {
                Vector3 bedPoint = Player2Profile.GetCustomSpawnPoint();
                Debug.Log($"[Splitscreen][Player] Spawn position: bed spawn point {bedPoint}");
                return bedPoint;
            }

            // Fallback: near Player 1
            Vector3 fallback = p1.transform.position + p1.transform.right * 2f;
            Debug.Log($"[Splitscreen][Player] Spawn position: fallback near P1 {fallback}");
            return fallback;
        }

        /// <summary>
        /// Remove the second player from the world.
        /// </summary>
        public void DespawnSecondPlayer()
        {
            if (Player2 == null)
            {
                Debug.Log("[Splitscreen][Player] DespawnSecondPlayer: Player2 is already null");
                return;
            }

            Debug.Log($"[Splitscreen][Player] Despawning Player 2 at {Player2.transform.position}");

            // Save Player 2's data and position before destroying
            if (Player2Profile != null)
            {
                Player2Profile.SavePlayerData(Player2);
                Player2Profile.SetLogoutPoint(Player2.transform.position);
                Player2Profile.Save();
                Debug.Log($"[Splitscreen][Player] Saved P2 profile and logout point");
            }

            // Destroy the networked object
            var nview = Player2.GetComponent<ZNetView>();
            if (nview != null && ZNetScene.instance != null)
            {
                ZNetScene.instance.Destroy(Player2.gameObject);
                Debug.Log("[Splitscreen][Player] Destroyed P2 via ZNetScene");
            }
            else
            {
                Destroy(Player2.gameObject);
                Debug.Log("[Splitscreen][Player] Destroyed P2 via Object.Destroy");
            }

            Player2 = null;
            Player2Controller = null;
            Debug.Log("[Splitscreen][Player] Player 2 despawned successfully");
        }

        /// <summary>
        /// Called each FixedUpdate to drive Player 2's controls from their gamepad.
        /// </summary>
        public void UpdatePlayer2Controls()
        {
            if (Player2 == null)
            {
                // Rate-limited warning
                if (Time.time - _lastP2AliveCheckTime > 5f)
                {
                    _lastP2AliveCheckTime = Time.time;
                    Debug.LogWarning("[Splitscreen][Player] UpdatePlayer2Controls: Player2 is null!");
                }
                return;
            }
            if (SplitScreenManager.Instance == null || !SplitScreenManager.Instance.SplitscreenActive) return;
            if (SplitInputManager.Instance == null) return;

            var input = SplitInputManager.Instance.GetInputState(1);
            if (input == null) return;

            IsUpdatingPlayer2 = true;

            // Build movement vector from Player 2's gamepad
            Vector3 moveDir = Vector3.zero;
            moveDir.x = input.MoveAxis.x;
            moveDir.z = -input.MoveAxis.y; // Valheim inverts Y on left stick

            if (moveDir.magnitude > 1f)
                moveDir.Normalize();

            // Map buttons to Valheim actions
            bool attack = input.RightShoulderDown;
            bool attackHold = input.RightShoulder;
            bool secondaryAttack = input.RightTrigger > 0.4f;
            bool secondaryAttackHold = secondaryAttack;
            bool block = input.LeftShoulderDown;
            bool blockHold = input.LeftShoulder;
            bool jump = input.ButtonSouthDown;
            bool crouch = input.RightStickPressDown;
            bool run = input.LeftStickPress;
            bool autoRun = false;
            bool dodge = input.ButtonEastDown;

            // Rate-limited logging of P2 controls
            if (Time.time - _lastControlsLogTime > 10f)
            {
                _lastControlsLogTime = Time.time;
                Debug.Log($"[Splitscreen][Player] P2 controls: move=({moveDir.x:F2},{moveDir.z:F2}), look=({input.LookAxis.x:F2},{input.LookAxis.y:F2}), atk={attack}, blk={block}, jmp={jump}, run={run}, dodge={dodge}");
                Debug.Log($"[Splitscreen][Player] P2 state: alive={!Player2.IsDead()}, health={Player2.GetHealth():F0}/{Player2.GetMaxHealth():F0}, pos={Player2.transform.position}");
            }

            Player2.SetControls(moveDir, attack, attackHold, secondaryAttack, secondaryAttackHold,
                block, blockHold, jump, crouch, run, autoRun, dodge);

            // Camera look for Player 2
            var config = SplitscreenPlugin.Instance.SplitConfig;
            float sens = config.GamepadSensitivity.Value;
            Vector2 look = new Vector2(
                input.LookAxis.x * 110f * Time.deltaTime * sens,
                -input.LookAxis.y * 110f * Time.deltaTime * sens
            );
            Player2.SetMouseLook(look);

            IsUpdatingPlayer2 = false;
        }

        /// <summary>
        /// Save Player 2's data (called periodically and on logout).
        /// </summary>
        public void SavePlayer2Profile()
        {
            if (Player2 == null || Player2Profile == null) return;
            Player2Profile.SavePlayerData(Player2);
            Player2Profile.Save();
        }

        private void FixedUpdate()
        {
            if (SplitScreenManager.Instance == null || !SplitScreenManager.Instance.SplitscreenActive) return;
            UpdatePlayer2Controls();
        }

        private void OnDestroy()
        {
            Instance = null;
        }
    }
}
