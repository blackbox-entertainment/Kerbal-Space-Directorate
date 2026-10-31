using UnityEngine;

namespace KSA.KSP.Events
{
    /// <summary>
    /// Hands-off recovery (Jira KSA-38): when the active vessel is landed or splashed on the home world with crew
    /// aboard and has been still for a few seconds, count down and recover it with stock recovery (refunds and
    /// science apply). Any input or the Cancel button aborts the countdown for that vessel.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class KsaAutoRecovery : MonoBehaviour
    {
        private const float StillSeconds = 10f;
        private const float CountdownSeconds = 10f;
        private const double MaxSpeed = 0.5;

        private float _stillSince = -1f;
        private float _countdownStart = -1f;
        private Vessel _target;
        private uint _cancelledVesselId;
        private ScreenMessage _message;

        private void Update()
        {
            if (KsaCore.Agency == null || !KsaCore.Agency.Settings.AutoRecover) return;
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null || !IsRecoverable(v))
            {
                Reset();
                return;
            }
            if (v.persistentId == _cancelledVesselId) return;

            if (_target != v) { Reset(); _target = v; }
            if (_stillSince < 0f) _stillSince = Time.realtimeSinceStartup;
            if (Time.realtimeSinceStartup - _stillSince < StillSeconds) return;

            if (_countdownStart < 0f)
            {
                _countdownStart = Time.realtimeSinceStartup;
                KsaCore.Log.Info("Auto-recovery: " + v.vesselName + " is down and still; recovering in " + CountdownSeconds + " s");
            }
            float left = CountdownSeconds - (Time.realtimeSinceStartup - _countdownStart);
            if (left > 0f)
            {
                string text = "[KSA] Mission complete. Recovering " + v.vesselName + " in " + Mathf.CeilToInt(left) + " s. Press Escape or Backspace to cancel.";
                if (_message == null) _message = ScreenMessages.PostScreenMessage(text, 1.5f, ScreenMessageStyle.UPPER_CENTER);
                else _message.message = text;
                if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Backspace))
                {
                    _cancelledVesselId = v.persistentId;
                    KsaCore.Log.Info("Auto-recovery cancelled by the player for " + v.vesselName);
                    ScreenMessages.PostScreenMessage("[KSA] Auto-recovery cancelled.", 3f, ScreenMessageStyle.UPPER_CENTER);
                    Reset();
                }
                return;
            }

            KsaCore.Log.Info("Auto-recovery: recovering " + v.vesselName);
            Reset();
            GameEvents.OnVesselRecoveryRequested.Fire(v);
        }

        private static bool IsRecoverable(Vessel v)
        {
            if (v.mainBody == null || !v.mainBody.isHomeWorld) return false;
            if (!(v.Landed || v.Splashed)) return false;
            if (v.situation == Vessel.Situations.PRELAUNCH) return false;
            if (v.GetCrewCount() == 0) return false;
            if (v.srfSpeed > MaxSpeed) return false;
            return true;
        }

        private void Reset()
        {
            _stillSince = -1f;
            _countdownStart = -1f;
            _target = null;
            _message = null;
        }
    }
}
