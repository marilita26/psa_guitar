using UnityEngine;

public class GripStrengthEstimator : MonoBehaviour
{
    [Header("Baseline")]
    public float baselineSec = 2.0f;

    [Header("Magnetic (use magnitude ONLY)")]
    [Tooltip("Μικρότερο => πιο ευαίσθητο. Π.χ. 2–10 για |B|.")]
    public float magScale = 2.7f;
    [Range(0.01f, 0.3f)] public float magLP = 0.10f;

    [Header("Motion gating (ignore motion)")]
    [Tooltip("Όσο μεγαλύτερο, τόσο πιο δύσκολα θεωρεί 'κίνηση'. Δοκίμασε 0.08–0.20.")]
    public float motionAccThresh = 0.18f;   // σε g units (περίπου)
    [Tooltip("Πόσο δυνατά 'κόβει' το mag όταν υπάρχει κίνηση.")]
    [Range(0f, 1f)] public float motionSuppress = 0.6f; // 1 = πλήρες κόψιμο
    [Range(0.01f, 0.3f)] public float motionLP = 0.12f;

    [Header("Touch (secondary)")]
    public float touchScale = 7f;
    [Range(1f, 3f)] public float touchCurve = 1f;
    [Range(0f, 1f)] public float wMag = 0.7f;
    [Range(0f, 1f)] public float wTouch = 0.3f;

    [Header("Output shaping")]
    [Range(0f, 0.5f)] public float deadZone = 0.03f;
    [Range(1f, 3f)] public float finalCurve = 1.2f;
    [Range(0f, 1f)] public float smoothAlpha = 0.25f;

    // ===== DEBUG EXPORTS =====
    [HideInInspector] public bool dbg_magLooksDead;
    [HideInInspector] public bool dbg_hasTouch;
    [HideInInspector] public float dbg_touchRadius;
    [HideInInspector] public float dbg_baselineTouch;

    [HideInInspector] public Vector3 dbg_rawB;
    [HideInInspector] public float dbg_Bmag;
    [HideInInspector] public float dbg_baselineBmag;
    [HideInInspector] public float dbg_smoothBmag;
    [HideInInspector] public float dbg_deltaMag;
    [HideInInspector] public float dbg_zMag;
    [HideInInspector] public float dbg_zTouch;

    [HideInInspector] public float dbg_accMag;
    [HideInInspector] public float dbg_motion;     // 0..1
    [HideInInspector] public bool dbg_isMoving;

    // State
    float baselineBmag = 0f;
    float smoothBmag = 0f;
    float baselineTouch = 0f;

    float smoothAccMag = 1f;
    bool baselineDone = false;
    float t0;

    public float GripStrengthPercent { get; private set; }

    void Start()
    {
        Input.compass.enabled = true;
        Input.location.Start(); // σημαντικό για compass σε Android

        // accelerometer υπάρχει by default στο Unity via Input.acceleration
        t0 = Time.realtimeSinceStartup;
        Debug.Log("[Grip v4] Start. (Orientation-safe |B| + motion gating)");
    }

    void Update()
    {
        float t = Time.realtimeSinceStartup - t0;

        // --- Magnetometer raw vector + magnitude ---
        Vector3 rawB = Input.compass.rawVector;
        bool magLooksDead = rawB.sqrMagnitude < 1e-6f;
        float Bmag = rawB.magnitude; // ΜΟΝΟ μέτρο (orientation-safe)

        // --- Touch radius (optional) ---
        bool hasTouch = Input.touchCount > 0;
        float touchRadius = 0f;

        if (hasTouch)
        {
            float sum = 0f;
            for (int i = 0; i < Input.touchCount; i++)
                sum += Input.GetTouch(i).radius;

            touchRadius = sum / Input.touchCount;

            // fallback αν radius=0
            if (touchRadius <= 0f) touchRadius = baselineTouch + 20f;
        }

        // --- Motion estimate from accelerometer ---
        // Input.acceleration ~ includes gravity, magnitude ~ 1g at rest
        float accMag = Input.acceleration.magnitude;
        smoothAccMag = Mathf.Lerp(smoothAccMag, accMag, motionLP);

        float accDeltaFrom1g = Mathf.Abs(smoothAccMag - 1f);

        // motion in 0..1 (soft)
        float motion = Mathf.InverseLerp(motionAccThresh, motionAccThresh * 2f, accDeltaFrom1g);
        motion = Mathf.Clamp01(motion);

        bool isMoving = motion > 0.01f;

        // --- Baseline phase ---
        if (!baselineDone)
        {
            if (t < baselineSec)
            {
                float a = 0.06f;
                baselineBmag = Mathf.Lerp(baselineBmag, Bmag, a);
                baselineTouch = Mathf.Lerp(baselineTouch, hasTouch ? touchRadius : 0f, a);

                smoothBmag = baselineBmag;

                ExportDebug(rawB, magLooksDead, hasTouch, touchRadius,
                            Bmag, baselineBmag, smoothBmag, 0f, 0f, 0f,
                            accMag, motion, isMoving);
                return;
            }

            baselineDone = true;
            Debug.Log($"[Grip v4] BaselineBmag={baselineBmag:F3}, baselineTouch={baselineTouch:F2}");
        }

        // --- Low-pass on B magnitude ---
        smoothBmag = Mathf.Lerp(smoothBmag, Bmag, magLP);

        // --- Magnetic deviation from baseline (magnitude only) ---
        float deltaMag = Mathf.Abs(smoothBmag - baselineBmag);
        float zMag = (magScale > 1e-6f) ? Mathf.Clamp01(deltaMag / magScale) : 0f;

        // --- Motion suppression: όταν σηκώνεις/κινείς, κόβει το μαγνητικό ---
        // motion=0 => καμία μείωση, motion=1 => πλήρης μείωση (ανάλογα με motionSuppress)
        float suppressFactor = 1f - (motionSuppress * motion);
        zMag *= Mathf.Clamp01(suppressFactor);

        // --- Touch deviation ---
        float zTouch = 0f;
        if (hasTouch)
        {
            float dT = Mathf.Max(0f, touchRadius - baselineTouch);
            zTouch = (touchScale > 1e-6f) ? Mathf.Clamp01(dT / touchScale) : 0f;
            zTouch = Mathf.Pow(zTouch, touchCurve);
        }

        // --- Fuse ---
        float wSum = Mathf.Max(wMag + wTouch, 1e-6f);
        float z = (wMag / wSum) * zMag + (wTouch / wSum) * zTouch;

        // --- deadZone + curve ---
        if (z < deadZone) z = 0f;
        else z = (z - deadZone) / (1f - deadZone);

        z = Mathf.Clamp01(z);
        z = Mathf.Pow(z, finalCurve);

        // --- smooth output ---
        float target = 100f * z;
        GripStrengthPercent = Mathf.Lerp(GripStrengthPercent, target, smoothAlpha);

        ExportDebug(rawB, magLooksDead, hasTouch, touchRadius,
                    Bmag, baselineBmag, smoothBmag, deltaMag, zMag, zTouch,
                    accMag, motion, isMoving);
    }

    void ExportDebug(Vector3 rawB, bool magLooksDead, bool hasTouch, float touchRadius,
                     float Bmag, float baselineBmag, float smoothBmag, float deltaMag,
                     float zMag, float zTouch,
                     float accMag, float motion, bool isMoving)
    {
        dbg_magLooksDead = magLooksDead;
        dbg_hasTouch = hasTouch;

        dbg_touchRadius = touchRadius;
        dbg_baselineTouch = baselineTouch;

        dbg_rawB = rawB;
        dbg_Bmag = Bmag;

        dbg_baselineBmag = baselineBmag;
        dbg_smoothBmag = smoothBmag;

        dbg_deltaMag = deltaMag;
        dbg_zMag = zMag;
        dbg_zTouch = zTouch;

        dbg_accMag = accMag;
        dbg_motion = motion;
        dbg_isMoving = isMoving;
    }
}
