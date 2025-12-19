using UnityEngine;

public class GripStrengthEstimator : MonoBehaviour
{
    [Header("Baseline")]
    public float baselineSec = 2.0f;

    [Header("Magnetic (use magnitude ONLY)")]
    [Tooltip("Μικρότερο => πιο ευαίσθητο. Π.χ. 2–10 για |B|.")]
    public float magScale = 0.8f;
    [Range(0.01f, 0.3f)] public float magLP = 0.10f;
    float slowBmag = 0f;   // very slow tracker for high-pass


    [Header("Motion gating (ignore motion)")]
    [Tooltip("Όσο μεγαλύτερο, τόσο πιο δύσκολα θεωρεί 'κίνηση'. Δοκίμασε 0.08–0.20.")]
    public float motionAccThresh = 0.18f;   
    [Tooltip("Πόσο δυνατά 'κόβει' το mag όταν υπάρχει κίνηση.")]
    [Range(0f, 1f)] public float motionSuppress = 0.6f; // 1 = πλήρες κόψιμο
    [Range(0.01f, 0.3f)] public float motionLP = 0.12f;

    [Header("Touch (secondary)")]
    public float touchScale = 5f;
    [Range(1f, 3f)] public float touchCurve = 1f;
    [Range(0f, 1f)] public float wMag = 0.6f;
    [Range(0f, 1f)] public float wTouch = 0.4f;

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
    [HideInInspector] public float dbg_motion;     
    [HideInInspector] public bool dbg_isMoving;

  
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
        Input.location.Start(); 

       
        t0 = Time.realtimeSinceStartup;
        Debug.Log("[Grip v4] Start. (Orientation-safe |B| + motion gating)");
    }

    void Update()
    {
        float t = Time.realtimeSinceStartup - t0;

        Vector3 rawB = Input.compass.rawVector;
        bool magLooksDead = rawB.sqrMagnitude < 1e-6f;
        float Bmag = rawB.magnitude; // ΜΟΝΟ μέτρο 

      
        bool hasTouch = Input.touchCount > 0;
        float touchRadius = 0f;

        if (hasTouch)
        {
            float sum = 0f;
            for (int i = 0; i < Input.touchCount; i++)
                sum += Input.GetTouch(i).radius;

            touchRadius = sum / Input.touchCount;

         
            if (touchRadius <= 0f) touchRadius = baselineTouch + 20f;
        }

        float accMag = Input.acceleration.magnitude;
        smoothAccMag = Mathf.Lerp(smoothAccMag, accMag, motionLP);


        float accDeltaFrom1g = Mathf.Abs(smoothAccMag - 1f);

       
        float motion = Mathf.InverseLerp(motionAccThresh, motionAccThresh * 2f, accDeltaFrom1g);
        motion = Mathf.Clamp01(motion);
        if (!hasTouch && motion < 0.05f)
        {
         
            baselineBmag = Mathf.Lerp(baselineBmag, Bmag, 0.02f);
            baselineTouch = Mathf.Lerp(baselineTouch, 0f, 0.05f);
        }


        bool isMoving = motion > 0.01f;

        //Baseline phase
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

        // Low-pass magnitude
        smoothBmag = Mathf.Lerp(smoothBmag, Bmag, magLP);
        
        slowBmag = Mathf.Lerp(slowBmag, Bmag, 0.01f);  
        float deltaMag = Mathf.Abs(smoothBmag - slowBmag); // HIGH-PASS Δ|B|

        float zMag = (magScale > 1e-6f) ? Mathf.Clamp01(deltaMag / magScale) : 0f;

        // Motion suppression: όταν σηκώνεις/κινείς, κόβει το μαγνητικό 
        // motion=0 => καμία μείωση, motion=1 => πλήρης μείωση
        float suppressFactor = 1f - (motionSuppress * motion);
        zMag *= Mathf.Clamp01(suppressFactor);

        float zTouch = 0f;
        if (hasTouch)
        {
            float dT = Mathf.Max(0f, touchRadius - baselineTouch);
            zTouch = (touchScale > 1e-6f) ? Mathf.Clamp01(dT / touchScale) : 0f;
            zTouch = Mathf.Pow(zTouch, touchCurve);
        }

        float z;

        // Αν δεν υπάρχει touch 
        if (!hasTouch)
        {
            z = zMag;   
        }
        else
        {
            // Αν υπάρχει touch 
            float wSum = Mathf.Max(wMag + wTouch, 1e-6f);
            z = (wMag / wSum) * zMag + (wTouch / wSum) * zTouch;
        }


        //deadZone + curve 
        if (z < deadZone) z = 0f;
        else z = (z - deadZone) / (1f - deadZone);

        z = Mathf.Clamp01(z);
        z = Mathf.Pow(z, finalCurve);

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
