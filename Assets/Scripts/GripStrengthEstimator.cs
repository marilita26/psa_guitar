using System;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class GripStrengthEstimator : MonoBehaviour
{
    [Header("UI (optional)")]
    public TextMeshProUGUI strengthText;

    [Header("Sampling / Window")]
    public int sampleRateGuess = 50;     // ~Hz
    public float windowSec = 1.0f;       // 1s window

    [Header("Feature Weights (0..1)")]
    [Range(0, 1)] public float wStdB = 0.30f;
    [Range(0, 1)] public float wPeakB = 0.20f;
    [Range(0, 1)] public float wAcc = 0.25f;
    [Range(0, 1)] public float wGyr = 0.15f;
    [Range(0, 1)] public float wTouch = 0.10f;

    [Header("Options")]
    public bool useTouchProxy = true;

    [Header("Fallback (χωρίς calibration)")]
    public bool autoBaseline = true;     // ΑΝΟΙΞΕ ΤΟ στο Inspector
    public float baselineSec = 3f;       // πόσα δευτερόλεπτα κάνει auto-calibration

    // εσωτερικές ουρές
    Queue<float> qB = new Queue<float>();
    Queue<Vector3> qA = new Queue<Vector3>();
    Queue<Vector3> qG = new Queue<Vector3>();

    // calibration means
    float stdB_light, stdB_medium, stdB_strong;
    float peakB_light, peakB_medium, peakB_strong;
    float acc_light, acc_medium, acc_strong;
    float gyr_light, gyr_medium, gyr_strong;
    float touch_light, touch_medium, touch_strong;

    // auto-baseline buffers
    float b0_std = 0, b0_peak = 0, a0_rms = 0, g0_rms = 0;
    bool baselineDone = false;
    float t0;

    public float GripStrengthPercent { get; private set; }

    int N => Mathf.Max(4, Mathf.FloorToInt(sampleRateGuess * windowSec));

    void Start()
    {
        Input.compass.enabled = true;
        Input.location.Start();
        Input.gyro.enabled = true;
        t0 = Time.realtimeSinceStartup;

        Debug.Log("[Grip] Ready. Δώσε άδεια Τοποθεσίας στο Android app για να δουλέψει το magnetometer.");
    }

    void Update()
    {
        // 1) Συλλογή δειγμάτων
        Vector3 rawB = Input.compass.rawVector;
        float magB = rawB.magnitude;

        if (float.IsNaN(magB) || magB <= 0f)
        {
            if (strengthText) strengthText.text = "Grip: --% (no mag)";
            return;
        }

        qB.Enqueue(magB);
        if (qB.Count > N) qB.Dequeue();

        Vector3 acc = Input.acceleration;
        qA.Enqueue(acc);
        if (qA.Count > N) qA.Dequeue();

        Vector3 gyr = Input.gyro.rotationRateUnbiased;
        qG.Enqueue(gyr);
        if (qG.Count > N) qG.Dequeue();

        if (qB.Count < N) return; // δεν έχει γεμίσει το παράθυρο

        // 2) Features παραθύρου
        float meanB, stdB, peakB;
        WindowStatsB(qB, out meanB, out stdB, out peakB);
        float rmsAcc = RMS(qA);
        float rmsGyr = RMS(qG);
        float meanTouch = useTouchProxy ? AvgTouchAreaThisFrame() : 0f;

        // 3) Auto-baseline (αν ΔΕΝ έχει γίνει calibration)
        if (autoBaseline && !baselineDone)
        {
            float t = Time.realtimeSinceStartup - t0;

            if (t < baselineSec)
            {
                // μαζεύουμε "ηρεμία"
                b0_std = Mathf.Lerp(b0_std, stdB, 0.05f);
                b0_peak = Mathf.Lerp(b0_peak, peakB, 0.05f);
                a0_rms = Mathf.Lerp(a0_rms, rmsAcc, 0.05f);
                g0_rms = Mathf.Lerp(g0_rms, rmsGyr, 0.05f);

                if (strengthText) strengthText.text = "Grip: --% (calibrating)";
                return;
            }
            else
            {
                // θέτουμε fake light/strong γύρω από το baseline
                stdB_light = b0_std;
                stdB_strong = b0_std + Mathf.Max(0.5f, b0_std * 0.8f);

                peakB_light = b0_peak;
                peakB_strong = b0_peak + Mathf.Max(0.5f, b0_peak * 0.8f);

                acc_light = a0_rms;
                acc_strong = a0_rms + Mathf.Max(0.2f, a0_rms * 0.6f);

                gyr_light = g0_rms;
                gyr_strong = g0_rms + Mathf.Max(0.2f, g0_rms * 0.6f);

                // touch αφήνουμε 0..κάτι ή το καλιμπράρεις με κουμπιά
                baselineDone = true;
                Debug.Log("[Grip] Auto-baseline completed.");
            }
        }

        // 4) Z-scores βάσει Light..Strong
        float zStdB = Z(stdB, stdB_light, stdB_strong);
        float zPeakB = Z(peakB, peakB_light, peakB_strong);
        float zAcc = Z(rmsAcc, acc_light, acc_strong);
        float zGyr = Z(rmsGyr, gyr_light, gyr_strong);
        float zTouch = useTouchProxy ? Z(meanTouch, touch_light, touch_strong) : 0f;

        // 5) Σύνθεση score
        float z = wStdB * zStdB + wPeakB * zPeakB + wAcc * zAcc + wGyr * zGyr + wTouch * zTouch;
        z = Mathf.Clamp01(z);
        GripStrengthPercent = 100f * z;

        if (strengthText)
            strengthText.text = $"Grip: {GripStrengthPercent:F0}%";
    }

    // ===== CALIBRATION API (κουμπιά Light/Medium/Strong) =====
    public void CaptureLightLevel(float seconds = 8f) => StartCoroutine(CaptureLevel(seconds, "light"));
    public void CaptureMediumLevel(float seconds = 8f) => StartCoroutine(CaptureLevel(seconds, "medium"));
    public void CaptureStrongLevel(float seconds = 8f) => StartCoroutine(CaptureLevel(seconds, "strong"));

    System.Collections.IEnumerator CaptureLevel(float seconds, string level)
    {
        float tStart = Time.realtimeSinceStartup;

        List<float> L_stdB = new(); List<float> L_peakB = new();
        List<float> L_acc = new(); List<float> L_gyr = new();
        List<float> L_touch = new();

        while (Time.realtimeSinceStartup - tStart < seconds)
        {
            if (qB.Count == N)
            {
                float meanB, stdB, peakB;
                WindowStatsB(qB, out meanB, out stdB, out peakB);
                L_stdB.Add(stdB);
                L_peakB.Add(peakB);
                L_acc.Add(RMS(qA));
                L_gyr.Add(RMS(qG));
                L_touch.Add(useTouchProxy ? AvgTouchAreaThisFrame() : 0f);
            }
            yield return null;
        }

        float mStdB = Mean(L_stdB);
        float mPeakB = Mean(L_peakB);
        float mAcc = Mean(L_acc);
        float mGyr = Mean(L_gyr);
        float mTouch = Mean(L_touch);

        switch (level)
        {
            case "light":
                stdB_light = mStdB; peakB_light = mPeakB;
                acc_light = mAcc; gyr_light = mGyr;
                touch_light = mTouch;
                break;
            case "medium":
                stdB_medium = mStdB; peakB_medium = mPeakB;
                acc_medium = mAcc; gyr_medium = mGyr;
                touch_medium = mTouch;
                break;
            case "strong":
                stdB_strong = mStdB; peakB_strong = mPeakB;
                acc_strong = mAcc; gyr_strong = mGyr;
                touch_strong = mTouch;
                break;
        }

        Debug.Log($"[Grip/Calib] {level}: stdB={mStdB:F3}, peakB={mPeakB:F3}, acc={mAcc:F3}, gyr={mGyr:F3}, touch={mTouch:F3}");
    }

    public void SaveCalibration()
    {
        Debug.Log($"[Grip/Calib] LIGHT  : stdB={stdB_light:F3}, peakB={peakB_light:F3}, acc={acc_light:F3}, gyr={gyr_light:F3}, touch={touch_light:F3}");
        Debug.Log($"[Grip/Calib] MEDIUM : stdB={stdB_medium:F3}, peakB={peakB_medium:F3}, acc={acc_medium:F3}, gyr={gyr_medium:F3}, touch={touch_medium:F3}");
        Debug.Log($"[Grip/Calib] STRONG : stdB={stdB_strong:F3}, peakB={peakB_strong:F3}, acc={acc_strong:F3}, gyr={gyr_strong:F3}, touch={touch_strong:F3}");
    }

    // ===== HELPERS =====
    static void WindowStatsB(Queue<float> q, out float mean, out float std, out float peak)
    {
        int n = q.Count;
        float sum = 0f, minv = float.MaxValue, maxv = float.MinValue;
        foreach (var v in q)
        {
            sum += v;
            if (v < minv) minv = v;
            if (v > maxv) maxv = v;
        }
        mean = sum / n;

        float var = 0f;
        foreach (var v in q)
        {
            float d = v - mean;
            var += d * d;
        }
        var /= n;
        std = Mathf.Sqrt(var);

        peak = maxv - minv;
    }

    static float RMS(Queue<Vector3> q)
    {
        int n = q.Count;
        if (n == 0) return 0f;
        double s2 = 0;
        foreach (var v in q) s2 += v.sqrMagnitude;
        return Mathf.Sqrt((float)(s2 / n));
    }

    static float Mean(List<float> L)
    {
        if (L == null || L.Count == 0) return 0f;
        double s = 0; foreach (var x in L) s += x;
        return (float)(s / L.Count);
    }

    static float Z(float x, float lightMean, float strongMean)
    {
        float denom = strongMean - lightMean;
        if (denom <= 1e-6f) return 0f;
        return Mathf.Clamp01((x - lightMean) / denom);
    }

    float AvgTouchAreaThisFrame()
    {
        if (!useTouchProxy) return 0f;
        if (Input.touchCount == 0) return 0f;

        float sum = 0f;
        for (int i = 0; i < Input.touchCount; i++)
        {
            var t = Input.GetTouch(i);
            sum += t.radius;
        }
        return sum / Input.touchCount;
    }
}
