using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using ValheimSplitscreen.Camera;
using ValheimSplitscreen.HUD;
using ValheimSplitscreen.Input;
using ValheimSplitscreen.Player;
using ValheimSplitscreen.UI;

namespace ValheimSplitscreen.Core
{
    public enum SplitscreenState
    {
        Disabled,           // No splitscreen
        PendingCharSelect,  // On main menu, P2 character select UI shown
        Armed,              // P2 character selected, waiting for world load
        Active              // In-game, splitscreen fully running
    }

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

        /// <summary>Current splitscreen state.</summary>
        public SplitscreenState State { get; set; } = SplitscreenState.Disabled;

        /// <summary>Backward-compatible property checked by all patches.</summary>
        public bool SplitscreenActive => State == SplitscreenState.Active;

        public int PlayerCount => SplitscreenActive ? 2 : 1;

        /// <summary>
        /// Which player index (0 or 1) is currently being processed for input/update.
        /// Many patches use this to route logic to the correct player.
        /// </summary>
        public int ActivePlayerIndex { get; set; }

        /// <summary>Profile selected for P2 on the main menu, used when world loads.</summary>
        public PlayerProfile PendingP2Profile { get; set; }

        // IMGUI styles for the armed indicator
        private GUIStyle _armedLabelStyle;
        private bool _armedStylesInit;

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
            if (Keyboard.current == null || !Keyboard.current.f10Key.wasPressedThisFrame)
                return;

            Debug.Log($"[Splitscreen] F10 pressed! State={State}, Game={Game.instance != null}, FejdStartup={FejdStartup.instance != null}");

            bool onMainMenu = FejdStartup.instance != null && Game.instance == null;

            if (onMainMenu)
            {
                HandleF10OnMainMenu();
            }
            else
            {
                HandleF10InGame();
            }
        }

        private void HandleF10OnMainMenu()
        {
            switch (State)
            {
                case SplitscreenState.Disabled:
                    // Open P2 character select
                    Debug.Log("[Splitscreen] Main menu: opening P2 character select");
                    State = SplitscreenState.PendingCharSelect;
                    CharacterSelect.IsMainMenuMode = true;
                    CharacterSelect.Show(
                        onSelected: (profile) =>
                        {
                            Debug.Log($"[Splitscreen] P2 character selected on menu: {(profile != null ? profile.GetName() : "CREATE NEW")}");
                            OnP2CharacterSelected(profile);
                        },
                        onCancelled: () =>
                        {
                            Debug.Log("[Splitscreen] P2 character selection cancelled on menu");
                            State = SplitscreenState.Disabled;
                        }
                    );
                    break;

                case SplitscreenState.PendingCharSelect:
                    // Cancel character selection
                    Debug.Log("[Splitscreen] Main menu: cancelling character select");
                    CharacterSelect.Hide();
                    State = SplitscreenState.Disabled;
                    PendingP2Profile = null;
                    break;

                case SplitscreenState.Armed:
                    // Disarm — cancel the pending splitscreen
                    Debug.Log("[Splitscreen] Main menu: disarming splitscreen");
                    State = SplitscreenState.Disabled;
                    PendingP2Profile = null;
                    break;

                case SplitscreenState.Active:
                    // Shouldn't be Active on main menu, but handle gracefully
                    Debug.LogWarning("[Splitscreen] State is Active on main menu — resetting to Disabled");
                    State = SplitscreenState.Disabled;
                    PendingP2Profile = null;
                    break;
            }
        }

        private void HandleF10InGame()
        {
            if (State == SplitscreenState.Active)
            {
                Debug.Log("[Splitscreen] In-game: deactivating splitscreen");
                DeactivateSplitscreen();
            }
            else if (State == SplitscreenState.Armed)
            {
                // Already armed with a profile — activate now if game is ready
                if (Game.instance != null && global::Player.m_localPlayer != null)
                {
                    Debug.Log("[Splitscreen] In-game: armed + ready, activating now");
                    OnWorldLoaded();
                }
                else
                {
                    Debug.LogWarning("[Splitscreen] In-game: armed but Game/Player not ready yet");
                }
            }
            else if (CharacterSelect != null && CharacterSelect.IsVisible)
            {
                Debug.Log("[Splitscreen] In-game: character select visible, ignoring F10");
            }
            else
            {
                // Legacy in-game flow: show character select then activate immediately
                Debug.Log("[Splitscreen] In-game: opening character select (legacy flow)");
                ShowCharacterSelectInGame();
            }
        }

        /// <summary>Legacy in-game flow: pick a character and activate immediately.</summary>
        private void ShowCharacterSelectInGame()
        {
            if (Game.instance == null)
            {
                Debug.LogWarning("[Splitscreen] ShowCharacterSelectInGame: Game.instance is NULL");
                return;
            }
            if (global::Player.m_localPlayer == null)
            {
                Debug.LogWarning("[Splitscreen] ShowCharacterSelectInGame: m_localPlayer is NULL");
                return;
            }

            CharacterSelect.IsMainMenuMode = false;
            CharacterSelect.Show(
                onSelected: (profile) =>
                {
                    Debug.Log($"[Splitscreen] Character selected in-game: {(profile != null ? profile.GetName() : "CREATE NEW")}");
                    ActivateSplitscreen(profile);
                },
                onCancelled: () => Debug.Log("[Splitscreen] Character selection cancelled in-game")
            );
        }

        /// <summary>Called when P2 selects a character on the main menu.</summary>
        public void OnP2CharacterSelected(PlayerProfile profile)
        {
            PendingP2Profile = profile;
            State = SplitscreenState.Armed;
            Debug.Log($"[Splitscreen] Armed! P2 profile='{(profile != null ? profile.GetName() : "CREATE NEW")}', waiting for world load");
        }

        /// <summary>
        /// Called when the world finishes loading and P1 has spawned.
        /// Triggers auto-activation if state is Armed.
        /// </summary>
        public void OnWorldLoaded()
        {
            if (State != SplitscreenState.Armed)
            {
                Debug.Log($"[Splitscreen] OnWorldLoaded called but State={State}, ignoring");
                return;
            }

            if (Game.instance == null || global::Player.m_localPlayer == null)
            {
                Debug.LogWarning("[Splitscreen] OnWorldLoaded: Game or Player not ready, ignoring");
                return;
            }

            Debug.Log($"[Splitscreen] OnWorldLoaded: auto-activating with P2 profile '{PendingP2Profile?.GetName()}'");
            ActivateSplitscreen(PendingP2Profile);
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

            State = SplitscreenState.Active;
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

            State = SplitscreenState.Disabled;
            PendingP2Profile = null;
            Debug.Log("[Splitscreen] ========== DEACTIVATION COMPLETE ==========");

            if (global::Player.m_localPlayer != null)
            {
                global::Player.m_localPlayer.Message(MessageHud.MessageType.Center, "Splitscreen deactivated!");
            }
        }

        /// <summary>
        /// Deactivate splitscreen but keep the Armed state so P2 doesn't
        /// need to re-select their character when loading another world.
        /// Called during Game.Shutdown when returning to main menu.
        /// </summary>
        public void DeactivateAndRearm()
        {
            if (!SplitscreenActive) return;

            var savedProfile = PendingP2Profile ?? PlayerManager?.Player2Profile;
            Debug.Log($"[Splitscreen] DeactivateAndRearm: saving profile '{savedProfile?.GetName()}' for re-arm");

            HudManager.OnSplitscreenDeactivated();
            Patches.HudPatches.RestoreHudAnchors();
            Patches.InventoryGuiPatches.RestoreInventoryCanvas();
            PlayerManager.DespawnSecondPlayer();
            CameraManager.OnSplitscreenDeactivated();
            InputManager.OnSplitscreenDeactivated();

            // Re-arm instead of going to Disabled
            PendingP2Profile = savedProfile;
            State = SplitscreenState.Armed;
            Debug.Log($"[Splitscreen] Re-armed with profile '{savedProfile?.GetName()}'");
        }

        private void OnGUI()
        {
            // Draw armed indicator on main menu
            if (State != SplitscreenState.Armed) return;
            if (FejdStartup.instance == null || Game.instance != null) return;

            if (!_armedStylesInit)
            {
                _armedLabelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 18,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.UpperLeft,
                    normal = { textColor = new Color(0.4f, 1f, 0.4f) }
                };
                _armedStylesInit = true;
            }

            string profileName = PendingP2Profile != null ? PendingP2Profile.GetName() : "New Character";
            string text = $"SPLITSCREEN ARMED\nP2: {profileName}\nPress F10 to cancel";

            float w = 300f;
            float h = 80f;
            float x = Screen.width - w - 20f;
            float y = 20f;

            // Background
            GUI.color = new Color(0, 0, 0, 0.6f);
            GUI.DrawTexture(new Rect(x - 10, y - 5, w + 20, h + 10), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(x, y, w, h), text, _armedLabelStyle);
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
