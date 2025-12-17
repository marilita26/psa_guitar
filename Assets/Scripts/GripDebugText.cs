using UnityEngine;
using TMPro;

public class GripDebugText : MonoBehaviour
{
    [Header("References")]
    public GripStrengthEstimator grip;   // σύρε εδώ το GripStrengthEstimator
    public TextMeshProUGUI debugText;    // σύρε εδώ το TMP Text

    void Update()
    {
        if (grip == null || debugText == null) return;
        debugText.text =
            $"Grip: {grip.GripStrengthPercent:F0}%\n" +
            $"MAG dead: {(grip.dbg_magLooksDead ? "YES" : "NO")}\n" +
            $"Bmag: {grip.dbg_Bmag:F2}\n" +
            $"Δ|B|: {grip.dbg_deltaMag:F3}  zM:{grip.dbg_zMag:F2}\n" +
            $"zTouch: {grip.dbg_zTouch:F2}  touch:{(grip.dbg_hasTouch ? "YES" : "NO")}" +
            $"\nMotion: {grip.dbg_motion:F2} (moving:{(grip.dbg_isMoving ? "YES" : "NO")})";

    }

}

