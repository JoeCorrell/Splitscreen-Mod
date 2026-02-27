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
        Disabled = 0,
        MenuSplit = 1,
        AwaitingP2Character = 2,
        PendingCharSelect = AwaitingP2Character, // legacy alias
        Armed = 3,
        Active = 4
    }

    /// <summary>
    /// Central coordinator for menu split flow, world-start gating, and in-game splitscreen systems.
    /// </summary>
    public class SplitScreenManager : MonoBehaviour
    {
        public static SplitScreenManager Instance { get; private set; }

        public SplitInputManager InputManager { get; private set; }
        public SplitCameraManager CameraManager { get; private set; }
        public SplitPlayerManager PlayerManager { get; private set; }
        public SplitHudManager HudManager { get; private set; }
        public SplitInventoryManager InventoryManager { get; private set; }
        public CharacterSelectUI CharacterSelect { get; private set; }
        public MenuSplitController MenuSplit { get; private set; }
        public Player2MenuOverlay P2Menu { get; private set; }

        public SplitscreenState State { get; private set; } = SplitscreenState.Disabled;
        public bool SplitscreenActive => State == SplitscreenState.Active;
        public bool SplitEnabled => _splitEnabled;
        public int PlayerCount => SplitscreenActive ? 2 : 1;

        /// <summary>
        /// Which player index (0 or 1) is currently being processed for input/update.
        /// Many patches use this to route logic to the correct player.
        /// </summary>
        public int ActivePlayerIndex { get; set; }

        /// <summary>Profile chosen for P2 during the world-start gate.</summary>
        public PlayerProfile PendingP2Profile { get; private set; }

        private bool _splitEnabled;
        private FejdStartup _deferredWorldStartOwner;
        private bool _allowNextWorldStart;

        private void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);

            Debug.Log("[Splitscreen] SplitScreenManager.Awake - creating subsystems");
            InputManager = gameObject.AddComponent<SplitInputManager>();
            CameraManager = gameObject.AddComponent<SplitCameraManager>();
            PlayerManager = gameObject.AddComponent<SplitPlayerManager>();
            HudManager = gameObject.AddComponent<SplitHudManager>();
            InventoryManager = gameObject.AddComponent<SplitInventoryManager>();
            CharacterSelect = gameObject.AddComponent<CharacterSelectUI>();
            MenuSplit = gameObject.AddComponent<MenuSplitController>();
            P2Menu = gameObject.AddComponent<Player2MenuOverlay>();
            Debug.Log("[Splitscreen] SplitScreenManager.Awake - all subsystems created OK");
        }

        private void Update()
        {
            if (Keyboard.current == null || !Keyboard.current.f10Key.wasPressedThisFrame)
                return;

            if (IsOnMainMenu())
            {
                if (_splitEnabled) DisableSplitMode();
                else EnableMenuSplit();
                return;
            }

            if (SplitscreenActive)
            {
                RequestDeactivate();
            }
        }

        public void EnableMenuSplit()
        {
            _splitEnabled = true;
            PendingP2Profile = null;
            _deferredWorldStartOwner = null;
            _allowNextWorldStart = false;

            if (!IsOnMainMenu())
            {
                Debug.Log("[Splitscreen] EnableMenuSplit requested outside main menu; split will activate when menu is loaded");
                return;
            }

            if (CharacterSelect != null && CharacterSelect.IsVisible)
            {
                CharacterSelect.Hide();
            }

            State = SplitscreenState.MenuSplit;
            EnsureMenuSplitActive();
            // Defer P2 clone creation by 1 frame so the canvas mode switch renders
            // at least one frame first, avoiding a black flash from the heavy clone work.
            StartCoroutine(DeferredShowP2CharSelect());
            Patches.MainMenuPatches.RefreshButtonState();
            Debug.Log("[Splitscreen] Menu split enabled (P2 top, P1 bottom)");
        }

        private IEnumerator DeferredShowP2CharSelect()
        {
            Debug.Log("[Splitscreen] Deferring P2 char select by 1 frame to avoid black flash");
            yield return null; // let one frame render so P1's half isn't black
            Debug.Log($"[Splitscreen] Deferred frame arrived, state={State}");
            if (State == SplitscreenState.MenuSplit)
                ShowP2CharacterSelectForMenuSplit();
            else
                Debug.Log($"[Splitscreen] Skipping P2 char select — state changed to {State}");
        }

        /// <summary>
        /// Show P2's character select in P2's half of the menu split.
        /// Unlike BeginP2CharacterSelection, this doesn't gate a world start —
        /// P2 picks their character while P1 browses the menu freely.
        /// </summary>
        private void ShowP2CharacterSelectForMenuSplit()
        {
            if (CharacterSelect == null) return;
            if (CharacterSelect.IsVisible) return;

            CharacterSelect.IsMainMenuMode = true;
            CharacterSelect.IsMenuSplitMode = true;
            CharacterSelect.Show(
                onSelected: profile =>
                {
                    PendingP2Profile = profile;
                    State = SplitscreenState.Armed;
                    if (MenuSplit != null)
                        MenuSplit.SetP2Ready(profile != null ? profile.GetName() : "New Character");
                    Patches.MainMenuPatches.RefreshButtonState();
                    Debug.Log($"[Splitscreen] P2 selected '{(profile != null ? profile.GetName() : "CREATE NEW")}' during menu split");
                },
                onCancelled: () =>
                {
                    // P2 cancelled — stay in MenuSplit, they can re-select
                    State = SplitscreenState.MenuSplit;
                    Patches.MainMenuPatches.RefreshButtonState();
                    Debug.Log("[Splitscreen] P2 cancelled character select during menu split");
                });
        }

        public void DisableSplitMode()
        {
            _splitEnabled = false;
            PendingP2Profile = null;
            _deferredWorldStartOwner = null;
            _allowNextWorldStart = false;

            if (CharacterSelect != null && CharacterSelect.IsVisible)
            {
                CharacterSelect.Hide();
            }

            if (SplitscreenActive)
            {
                DeactivateSplitscreen(keepMenuSplit: false);
                Patches.MainMenuPatches.RefreshButtonState();
                return;
            }

            if (MenuSplit != null && MenuSplit.IsActive)
            {
                MenuSplit.Deactivate();
            }

            State = SplitscreenState.Disabled;
            Patches.MainMenuPatches.RefreshButtonState();
            Debug.Log("[Splitscreen] Split mode disabled");
        }

        /// <summary>
        /// Called by main-menu patches whenever Fejd UI is shown/rebuilt.
        /// Keeps menu split persistent while split mode is enabled.
        /// </summary>
        public void RefreshMainMenuSplit()
        {
            if (!_splitEnabled) return;
            if (!IsOnMainMenu()) return;

            if (State == SplitscreenState.Disabled || State == SplitscreenState.Armed)
            {
                State = SplitscreenState.MenuSplit;
            }

            if (State == SplitscreenState.MenuSplit)
            {
                EnsureMenuSplitActive();
                ShowP2CharacterSelectForMenuSplit();
            }
        }

        /// <summary>
        /// Called by FejdStartup.OnWorldStart prefix.
        /// Returns true to block the original world-start call and open P2 character selection first.
        /// </summary>
        public bool TryGateWorldStart(FejdStartup fejd)
        {
            if (!_splitEnabled) return false;
            if (SplitscreenActive) return false;

            // One-shot bypass when we re-invoke OnWorldStart after P2 selection.
            if (ConsumeWorldStartBypass(fejd))
            {
                return false;
            }

            if (State == SplitscreenState.AwaitingP2Character)
            {
                return true;
            }

            // P2 already selected their character — allow the world start to proceed.
            if (State == SplitscreenState.Armed && PendingP2Profile != null)
            {
                Debug.Log("[Splitscreen] P2 already armed, allowing world start");
                return false;
            }

            if (State != SplitscreenState.MenuSplit)
            {
                if (!IsOnMainMenu()) return false;
                State = SplitscreenState.MenuSplit;
            }

            _deferredWorldStartOwner = fejd;
            BeginP2CharacterSelection(fejd);
            return true;
        }

        /// <summary>
        /// Called by FejdStartup.OnWorldStart prefix before TryGateWorldStart.
        /// </summary>
        public bool ConsumeWorldStartBypass(FejdStartup fejd)
        {
            if (!_allowNextWorldStart) return false;
            if (_deferredWorldStartOwner != null && fejd != _deferredWorldStartOwner) return false;

            _allowNextWorldStart = false;
            _deferredWorldStartOwner = null;
            return true;
        }

        private void BeginP2CharacterSelection(FejdStartup fejd)
        {
            EnsureMenuSplitActive();
            State = SplitscreenState.AwaitingP2Character;

            CharacterSelect.IsMainMenuMode = true;
            CharacterSelect.IsMenuSplitMode = true;
            CharacterSelect.Show(
                onSelected: profile => OnP2CharacterSelected(profile, fejd),
                onCancelled: OnP2CharacterSelectionCancelled);

            Patches.MainMenuPatches.RefreshButtonState();
            Debug.Log("[Splitscreen] World start gated: waiting for P2 character selection");
        }

        private void OnP2CharacterSelectionCancelled()
        {
            PendingP2Profile = null;
            State = SplitscreenState.MenuSplit;
            EnsureMenuSplitActive();
            Patches.MainMenuPatches.RefreshButtonState();
            Debug.Log("[Splitscreen] P2 character selection cancelled; world start remains blocked");
        }

        private void OnP2CharacterSelected(PlayerProfile profile, FejdStartup fejd)
        {
            PendingP2Profile = profile;
            State = SplitscreenState.Armed;
            Patches.MainMenuPatches.RefreshButtonState();

            Debug.Log($"[Splitscreen] P2 selected '{(profile != null ? profile.GetName() : "CREATE NEW")}', resuming original world start");
            ResumeDeferredWorldStart(fejd);
        }

        private void ResumeDeferredWorldStart(FejdStartup fejd)
        {
            if (fejd == null)
            {
                Debug.LogWarning("[Splitscreen] ResumeDeferredWorldStart: FejdStartup is null");
                State = SplitscreenState.MenuSplit;
                return;
            }

            _deferredWorldStartOwner = fejd;
            _allowNextWorldStart = true;

            try
            {
                fejd.OnWorldStart();
            }
            catch (System.Exception ex)
            {
                _allowNextWorldStart = false;
                _deferredWorldStartOwner = null;
                State = SplitscreenState.MenuSplit;
                Debug.LogError($"[Splitscreen] ResumeDeferredWorldStart failed: {ex}");
            }
        }

        /// <summary>
        /// Called when P1 has spawned in the world while state is Armed.
        /// </summary>
        public void OnWorldLoaded()
        {
            if (State != SplitscreenState.Armed)
            {
                return;
            }

            if (Game.instance == null || global::Player.m_localPlayer == null)
            {
                Debug.LogWarning("[Splitscreen] OnWorldLoaded: Game or Player not ready yet");
                return;
            }

            if (MenuSplit != null && MenuSplit.IsActive)
            {
                MenuSplit.Deactivate();
            }

            ActivateSplitscreen(PendingP2Profile);
            Patches.MainMenuPatches.RefreshButtonState();
        }

        /// <summary>
        /// Activates in-game splitscreen and spawns P2 with the selected profile.
        /// </summary>
        public void ActivateSplitscreen(PlayerProfile selectedProfile)
        {
            if (SplitscreenActive)
            {
                Debug.LogWarning("[Splitscreen] ActivateSplitscreen called while already active");
                return;
            }

            if (Game.instance == null)
            {
                Debug.LogWarning("[Splitscreen] ActivateSplitscreen: Game.instance is null");
                return;
            }

            State = SplitscreenState.Active;

            Debug.Log("[Splitscreen] ========== ACTIVATING SPLITSCREEN ==========");
            try
            {
                InputManager.OnSplitscreenActivated();
                CameraManager.OnSplitscreenActivated();
                PlayerManager.SpawnSecondPlayer(selectedProfile);
                HudManager.OnSplitscreenActivated();
                InventoryManager.OnSplitscreenActivated();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Splitscreen] Activation failed: {ex}");
            }
            Debug.Log("[Splitscreen] ========== ACTIVATION COMPLETE ==========");
        }

        public void RequestDeactivate()
        {
            _splitEnabled = false;
            if (SplitscreenActive)
            {
                DeactivateSplitscreen(keepMenuSplit: false);
            }
            else
            {
                DisableSplitMode();
            }
            Patches.MainMenuPatches.RefreshButtonState();
        }

        /// <summary>
        /// Called from Game.Shutdown patch so returning to menu keeps split mode enabled.
        /// </summary>
        public void DeactivateAndRearm()
        {
            DeactivateToMenuSplit();
        }

        public void DeactivateToMenuSplit()
        {
            if (!SplitscreenActive)
            {
                if (_splitEnabled)
                {
                    StartCoroutine(DelayedMenuSplitRestore());
                }
                return;
            }

            DeactivateSplitscreen(keepMenuSplit: true);

            PendingP2Profile = null;
            _allowNextWorldStart = false;
            _deferredWorldStartOwner = null;
            State = _splitEnabled ? SplitscreenState.MenuSplit : SplitscreenState.Disabled;
            Patches.MainMenuPatches.RefreshButtonState();

            if (_splitEnabled)
            {
                StartCoroutine(DelayedMenuSplitRestore());
            }
        }

        private IEnumerator DelayedMenuSplitRestore()
        {
            yield return null;
            yield return new WaitForSeconds(0.35f);

            if (_splitEnabled && IsOnMainMenu())
            {
                State = SplitscreenState.MenuSplit;
                EnsureMenuSplitActive();
                Patches.MainMenuPatches.RefreshButtonState();
                Debug.Log("[Splitscreen] Menu split restored");
            }
        }

        private void DeactivateSplitscreen(bool keepMenuSplit)
        {
            if (!SplitscreenActive) return;

            Debug.Log("[Splitscreen] ========== DEACTIVATING SPLITSCREEN ==========");

            if (P2Menu != null) P2Menu.Hide();
            HudManager.OnSplitscreenDeactivated();
            Patches.HudPatches.RestoreHudAnchors();
            Patches.InventoryGuiPatches.RestoreInventoryCanvas();
            InventoryManager.OnSplitscreenDeactivated();
            PlayerManager.DespawnSecondPlayer();
            CameraManager.OnSplitscreenDeactivated();
            InputManager.OnSplitscreenDeactivated();

            PendingP2Profile = null;

            if (keepMenuSplit && _splitEnabled && IsOnMainMenu())
            {
                State = SplitscreenState.MenuSplit;
                EnsureMenuSplitActive();
            }
            else
            {
                State = SplitscreenState.Disabled;
                if (MenuSplit != null && MenuSplit.IsActive)
                {
                    MenuSplit.Deactivate();
                }
            }
            Patches.MainMenuPatches.RefreshButtonState();

            Debug.Log("[Splitscreen] ========== DEACTIVATION COMPLETE ==========");
        }

        private bool IsOnMainMenu()
        {
            return FejdStartup.instance != null && Game.instance == null;
        }

        private void EnsureMenuSplitActive()
        {
            if (!IsOnMainMenu()) return;
            if (MenuSplit == null) return;
            if (MenuSplit.IsActive) return;

            MenuSplit.Activate(horizontal: true);
        }

        private void OnDestroy()
        {
            if (SplitscreenActive)
            {
                DeactivateSplitscreen(keepMenuSplit: false);
            }

            Instance = null;
        }
    }
}
