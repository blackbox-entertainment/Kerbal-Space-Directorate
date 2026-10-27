using KSA.KSP.Roster;
using KSP.UI.Screens;
using UnityEngine;

namespace KSA.KSP.UI
{
    /// <summary>
    /// Adds the KSA button to the stock AppLauncher toolbar at the Space Center (Jira KSA-13)
    /// and toggles the Director's Office. Flight and Editor scenes come later.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.SpaceCentre, false)]
    public class KsaToolbar : MonoBehaviour
    {
        /// <summary>GameDatabase URL of the toolbar icon: GameData/KSA/Textures/toolbar.png (no extension).</summary>
        private const string IconUrl = "KSA/Textures/toolbar";
        private const int IconSize = 38;
        private static Texture _icon;

        private readonly Core.ILogger _log = new KspLogger();
        private ApplicationLauncherButton _button;
        private DirectorOfficeWindow _window;

        private void Awake()
        {
            GameEvents.onGUIApplicationLauncherReady.Add(OnAppLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Add(OnAppLauncherDestroyed);
        }

        private void Start()
        {
            // The launcher may already be up by the time this addon starts.
            if (ApplicationLauncher.Ready) OnAppLauncherReady();
        }

        private void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(OnAppLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Remove(OnAppLauncherDestroyed);
            if (_window != null) { _window.Close(); _window = null; }
            RemoveButton();
        }

        private void OnAppLauncherReady()
        {
            if (_button != null || ApplicationLauncher.Instance == null) return;
            _button = ApplicationLauncher.Instance.AddModApplication(
                OnButtonOn, OnButtonOff,
                null, null, null, null,
                ApplicationLauncher.AppScenes.SPACECENTER,
                GetIcon());
            _log.Info("Toolbar button added (Space Center)");
        }

        private void OnAppLauncherDestroyed()
        {
            RemoveButton();
        }

        private void RemoveButton()
        {
            if (_button == null) return;
            if (ApplicationLauncher.Instance != null) ApplicationLauncher.Instance.RemoveModApplication(_button);
            _button = null;
        }

        private void OnButtonOn()
        {
            if (KsaCore.Agency == null)
            {
                _log.Warn("Toolbar: no agency loaded yet");
                if (_button != null) _button.SetFalse(false);
                return;
            }
            if (_window == null) _window = new DirectorOfficeWindow(KsaCore.Agency, OnWindowClosedByUser);
            _window.Open();
        }

        private void OnButtonOff()
        {
            if (_window != null) _window.Close();
        }

        private void OnWindowClosedByUser()
        {
            // Keep the toolbar button's toggle state in sync when the user closes the window itself.
            if (_button != null) _button.SetFalse(false);
        }

        /// <summary>
        /// Loads the icon PNG that KSP already read from GameData at startup.
        /// Falls back to a code-drawn tile if the texture is missing, so the button always appears.
        /// </summary>
        private Texture GetIcon()
        {
            if (_icon != null) return _icon;

            Texture2D fromDisk = GameDatabase.Instance.GetTexture(IconUrl, false);
            if (fromDisk != null)
            {
                _icon = fromDisk;
                return _icon;
            }

            _log.Warn("Toolbar icon '" + IconUrl + "' not found in GameDatabase; using fallback tile");
            _icon = DrawFallbackIcon();
            return _icon;
        }

        private static Texture2D DrawFallbackIcon()
        {
            var tex = new Texture2D(IconSize, IconSize, TextureFormat.ARGB32, false);
            tex.hideFlags = HideFlags.HideAndDontSave;
            var fill = new Color(0.12f, 0.35f, 0.75f, 1f);
            var white = Color.white;
            for (int y = 0; y < IconSize; y++)
            {
                for (int x = 0; x < IconSize; x++)
                {
                    bool border = x < 2 || y < 2 || x >= IconSize - 2 || y >= IconSize - 2;
                    bool bar = y >= 16 && y < 22 && x >= 8 && x < IconSize - 8;
                    tex.SetPixel(x, y, (border || bar) ? white : fill);
                }
            }
            tex.Apply();
            return tex;
        }
    }
}
