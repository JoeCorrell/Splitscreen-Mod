using UnityEngine;
using UnityEngine.InputSystem;
using ValheimSplitscreen.Camera;
using ValheimSplitscreen.HUD;
using ValheimSplitscreen.Input;
using ValheimSplitscreen.Player;
using ValheimSplitscreen.UI;

namespace ValheimSplitscreen.Core
{
    /// <summary>
    /// Central manager that coordinates all splitscreen subsystems.
    /// Attached to the plugin GameObject so it persists across scenes.
    /// </summary>
    public class SplitScreenManager : MonoBehaviour
    {
        public static SplitScreenManager Instance { get; private set; }

        public SplitInputManager InputManager { get; private set; }
        public SplitCameraManager CameraManager { get; private set; }
        public SplitPlayerManager PlayerManager { get; private set; }
        public SplitHudManager HudManager { get; private set; }
        public CharacterSelectUI CharacterSelect { get; private set; }

        public bool SplitscreenActive { get; private set; }
        public int PlayerCount => SplitscreenActive ? 2 : 1;

        /// <summary>
        /// Which player index (0 or 1) is currently being processed for input/update.
        /// Many patches use this to route logic to the correct player.
        /// </summary>
        public int ActivePlayerIndex { get; set; }

        private void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);

            Debug.Log("[Splitscreen] SplitScreenManager.Awake - creating subsystems");
            InputManager = gameObject.AddComponent<SplitInputManager>();
            CameraManager = gameObject.AddComponent<SplitCameraManager>();
            PlayerManager = gameObject.AddComponent<SplitPlayerManager>();
            HudManager = gameObject.AddComponent<SplitHudManager>();
            CharacterSelect = gameObject.AddComponent<CharacterSelectUI>();
            Debug.Log("[Splitscreen] SplitScreenManager.Awake - all subsystems created OK");
        }

        private void Update()
        {
            // Check for splitscreen toggle hotkey
            if (Keyboard.current != null && Keyboard.current.f10Key.wasPressedThisFrame)
            {
                var lp = global::Player.m_localPlayer;
                Debug.Log($"[Splitscreen] F10 pressed! Active={SplitscreenActive}, CharSelectVisible={CharacterSelect?.IsVisible}, Game={Game.instance != null}, LocalPlayer={lp != null}");
                ToggleSplitscreen();
            }
        }

        public void ToggleSplitscreen()
        {
            if (SplitscreenActive)
            {
                Debug.Log("[Splitscreen] Toggle -> Deactivating");
                DeactivateSplitscreen();
            }
            else if (CharacterSelect != null && CharacterSelect.IsVisible)
            {
                Debug.Log("[Splitscreen] Toggle -> CharSelect already visible, ignoring");
            }
            else
            {
                Debug.Log("[Splitscreen] Toggle -> Opening character select");
                ShowCharacterSelect();
            }
        }

        private void ShowCharacterSelect()
        {
            if (Game.instance == null)
            {
                Debug.LogWarning("[Splitscreen] ShowCharacterSelect: Game.instance is NULL - aborting");
                return;
            }
            if (global::Player.m_localPlayer == null)
            {
                Debug.LogWarning("[Splitscreen] ShowCharacterSelect: m_localPlayer is NULL - aborting");
                return;
            }

            Debug.Log("[Splitscreen] Opening character selection UI");
            CharacterSelect.Show(
                onSelected: (profile) =>
                {
                    Debug.Log($"[Splitscreen] Character selected: {(profile != null ? profile.GetName() : "CREATE NEW")}");
                    ActivateSplitscreen(profile);
                },
                onCancelled: () => Debug.Log("[Splitscreen] Character selection cancelled by user")
            );
        }

        /// <summary>
        /// Activate splitscreen with the given profile for Player 2.
        /// Pass null to create a new "splitscreen_p2" profile.
        /// </summary>
        public void ActivateSplitscreen(PlayerProfile selectedProfile)
        {
            if (SplitscreenActive)
            {
                Debug.LogWarning("[Splitscreen] ActivateSplitscreen called but already active! Ignoring.");
                return;
            }
            if (Game.instance == null)
            {
                Debug.LogWarning("[Splitscreen] ActivateSplitscreen: Game.instance is null! Aborting.");
                return;
            }

            int gpCount = Gamepad.all.Count;

            SplitscreenActive = true;
            Debug.Log($"[Splitscreen] ========== ACTIVATING SPLITSCREEN ==========");
            Debug.Log($"[Splitscreen]   Gamepads: {gpCount}");
            Debug.Log($"[Splitscreen]   Profile: {(selectedProfile != null ? selectedProfile.GetName() + " (" + selectedProfile.GetFilename() + ")" : "CREATE NEW")}");
            var p1Pre = global::Player.m_localPlayer;
            Debug.Log($"[Splitscreen]   P1 (m_localPlayer): {p1Pre?.GetPlayerName()} at {p1Pre?.transform.position}");

            try
            {
                Debug.Log("[Splitscreen]   Step 1/4: InputManager.OnSplitscreenActivated");
                InputManager.OnSplitscreenActivated();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Splitscreen] Step 1 FAILED: {ex}");
            }

            try
            {
                Debug.Log("[Splitscreen]   Step 2/4: CameraManager.OnSplitscreenActivated");
                CameraManager.OnSplitscreenActivated();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Splitscreen] Step 2 FAILED: {ex}");
            }

            try
            {
                Debug.Log("[Splitscreen]   Step 3/4: PlayerManager.SpawnSecondPlayer");
                PlayerManager.SpawnSecondPlayer(selectedProfile);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Splitscreen] Step 3 FAILED: {ex}");
            }

            try
            {
                Debug.Log("[Splitscreen]   Step 4/4: HudManager.OnSplitscreenActivated");
                HudManager.OnSplitscreenActivated();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Splitscreen] Step 4 FAILED: {ex}");
            }

            var p1Post = global::Player.m_localPlayer;
            var p2Post = PlayerManager.Player2;
            Debug.Log($"[Splitscreen]   P1: {p1Post?.GetPlayerName()} alive={p1Post != null}");
            Debug.Log($"[Splitscreen]   P2: {p2Post?.GetPlayerName()} alive={p2Post != null} at {p2Post?.transform.position}");
            Debug.Log($"[Splitscreen] ========== ACTIVATION COMPLETE ==========");

            if (global::Player.m_localPlayer != null)
            {
                string inputInfo;
                if (gpCount >= 2)
                    inputInfo = "P1=Gamepad1, P2=Gamepad2";
                else if (gpCount == 1)
                    inputInfo = "P1=KB+Mouse, P2=Gamepad";
                else
                    inputInfo = "P1=KB+Mouse, P2=IJKL+Numpad";

                global::Player.m_localPlayer.Message(MessageHud.MessageType.Center,
                    $"Splitscreen active! ({inputInfo})");
            }
        }

        private void DeactivateSplitscreen()
        {
            if (!SplitscreenActive) return;

            SplitscreenActive = false;
            Debug.Log("[Splitscreen] ========== DEACTIVATING SPLITSCREEN ==========");

            Debug.Log("[Splitscreen]   Step 1: HudManager + RestoreHudAnchors");
            HudManager.OnSplitscreenDeactivated();
            Patches.HudPatches.RestoreHudAnchors();
            Patches.InventoryGuiPatches.RestoreInventoryCanvas();

            Debug.Log("[Splitscreen]   Step 2: PlayerManager.DespawnSecondPlayer");
            PlayerManager.DespawnSecondPlayer();

            Debug.Log("[Splitscreen]   Step 3: CameraManager.OnSplitscreenDeactivated");
            CameraManager.OnSplitscreenDeactivated();

            Debug.Log("[Splitscreen]   Step 4: InputManager.OnSplitscreenDeactivated");
            InputManager.OnSplitscreenDeactivated();

            Debug.Log("[Splitscreen] ========== DEACTIVATION COMPLETE ==========");

            if (global::Player.m_localPlayer != null)
            {
                global::Player.m_localPlayer.Message(MessageHud.MessageType.Center, "Splitscreen deactivated!");
            }
        }

        private void OnDestroy()
        {
            if (SplitscreenActive)
            {
                DeactivateSplitscreen();
            }
            Instance = null;
        }
    }
}
