using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class GripStrengthEstimator : MonoBehaviour
{
    [Header("UI")]
    public TextMeshProUGUI strengthText;   // Σύρε εδώ το TextMeshPro από το Canvas

    [Header("Παράθυρο / Baseline")]
    public int sampleRateGuess = 50;       // ~fps
    public float windowSec = 1.0f;         // πόσο "πίσω" κοιτάμε (δευτ)
    public float baselineSec = 3.0f;       // πόσο κρατάει το αρχικό baseline
    public float gain = 3.0f;              // πόσο "ευαίσθητη" είναι η κλίμακα

    Queue<float> qB = new Queue<float>();  // αποθηκεύουμε |B| στο παράθυρο

    float baselineStd = 0f;                // std(|B|) σε ηρεμία
    bool baselineDone = false;
    float t0;

    public float GripStrengthPercent { get; private set; }

    int N => Mathf.Max(4, Mathf.FloorToInt(sampleRateGuess * windowSec));

    void Start()
    {
        Input.compass.enabled = true;
        Input.location.Start(); // βοηθάει να "ξυπνήσει" το magnetometer
        t0 = Time.realtimeSinceStartup;

        Debug.Log("[GripSimple] Ready. Δώσε άδεια Τοποθεσίας στο app για να δουλέψει το magnetometer.");
    }

    void Update()
    {
        // 1) Διάβασε μαγνητικό πεδίο
        Vector3 rawB = Input.compass.rawVector;
        float magB = rawB.magnitude;

        if (float.IsNaN(magB) || magB <= 0f)
        {
            if (strengthText) strengthText.text = "Grip: --% (no mag)";
            return;
        }

        // 2) Γέμισε το κυλιόμενο παράθυρο
        qB.Enqueue(magB);
        if (qB.Count > N) qB.Dequeue();

        if (qB.Count < N)
        {
            if (strengthText) strengthText.text = "Grip: --% (warming up)";
            return;
        }

        // 3) Υπολόγισε std(|B|) στο παράθυρο
        float meanB, stdB;
        WindowStats(qB, out meanB, out stdB);

        float t = Time.realtimeSinceStartup - t0;

        // 4) Πρώτα baseline (ηρεμία)
        if (!baselineDone)
        {
            if (t < baselineSec)
            {
                // κάνουμε moving average για baselineStd
                baselineStd = Mathf.Lerp(baselineStd, stdB, 0.05f);
                if (strengthText) strengthText.text = "Grip: --% (calibrating)";
                return;
            }
            else
            {
                baselineDone = true;
                if (baselineStd < 1e-6f)
                    baselineStd = 1e-3f; // για να μην μηδενιστεί τελείως

                Debug.Log($"[GripSimple] Baseline std = {baselineStd:F6}");
            }
        }

        // 5) Υπολόγισε GripStrength μόνο από το πόσο μεγαλύτερη std έχεις από την baseline
        float excess = stdB - baselineStd;          // πόσο πιο "νευρικό" είναι τώρα
        float denom = baselineStd * gain + 1e-6f;   // όσο μεγαλύτερο gain, τόσο πιο εύκολα πιάνει 100%

        float z = excess / denom;                   // αν excess = gain * baselineStd → z ≈ 1
        z = Mathf.Clamp01(z);

        GripStrengthPercent = 100f * z;

        if (strengthText)
            strengthText.text = $"Grip (mag): {GripStrengthPercent:F0}%";
    }

    // ========== HELPERS ==========

    static void WindowStats(Queue<float> q, out float mean, out float std)
    {
        int n = q.Count;
        float sum = 0f;

        foreach (var v in q) sum += v;
        mean = sum / n;

        float var = 0f;
        foreach (var v in q)
        {
            float d = v - mean;
            var += d * d;
        }
        var /= n;
        std = Mathf.Sqrt(var);
    }
}
