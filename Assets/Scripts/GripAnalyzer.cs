using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using TMPro; // αν δεν χρησιμοποιείς TextMeshPro, βγάλε τις αναφορές UI

public class GripAnalyzer : MonoBehaviour
{
    [Header("References")]
    public EMFlog emf;                  // σύρε εδώ το EMFlog από το Hierarchy

    [Header("UI (optional)")]
    public TextMeshProUGUI statusText;  // δείξε "Stable"/"Unstable"
    public TextMeshProUGUI meanText;    // mean |B|
    public TextMeshProUGUI stdText;     // std |B|
    public TextMeshProUGUI peakText;    // peak-to-peak ΔB
    public TextMeshProUGUI giText;      // Grip Index

    [Header("Analysis Settings")]
    public int sampleRateGuess = 50;    // ~Hz (χρησιμοποιούμε frame-rate proxy)
    public float windowSec = 1.0f;      // μήκος παραθύρου για features
    public float baselineSec = 3.0f;    // αρχικά δευτερόλεπτα για calibration
    public float gripK = 1.5f;          // ευαισθησία threshold (IQR multiplier)

    Queue<float> winMag = new Queue<float>();
    float tStart;
    bool running = false;

    // baseline stats
    List<float> baselineGI = new List<float>();
    float mu0 = 0f, sigma0 = 1f;
    float thGrip = float.PositiveInfinity; // adaptive threshold

    // τρέχοντα features
    float curMean = 0f, curStd = 0f, curPeak = 0f, curGI = 0f;

    // logging summary για κάθε run
    List<string> summaryLines = new List<string>();
    int Nwin => Mathf.Max(4, Mathf.FloorToInt(sampleRateGuess * windowSec));

    void Start()
    {
        tStart = Time.realtimeSinceStartup;
        winMag.Clear();
        baselineGI.Clear();
        summaryLines.Clear();
        summaryLines.Add("phase,t_ms,mean,std,peak2peak,GI,decision");
    }

    void Update()
    {
        if (emf == null) return;

        // "τρέχουμε" αν ο EMFlog γράφει (δανειζόμαστε το ίδιο flag)
        // δεν έχει public flag, οπότε θα πάρουμε το τελευταίο δείγμα από το αρχείο samples μέσω helper
        // Λύση: υπολόγισε από rawVector κάθε frame εδώ (ανεξάρτητο από EMFlog)
        Vector3 rawB = Input.compass.rawVector;
        float mag = rawB.magnitude;

        // αν δεν έχει ενεργοποιηθεί ο compass, απλά μην κάνεις ανάλυση
        if (float.IsNaN(mag) || mag <= 0f) return;

        running = true; // μόλις βλέπουμε έγκυρες τιμές

        // push σε κυλιόμενο παράθυρο
        winMag.Enqueue(mag);
        while (winMag.Count > Nwin) winMag.Dequeue();

        if (winMag.Count < Nwin) return; // περίμενε να γεμίσει το παράθυρο

        // features
        ComputeWindowFeatures(winMag, out curMean, out curStd, out curPeak);
        curGI = GripIndex(curPeak, curStd, curMean, mu0);

        float tNow = (Time.realtimeSinceStartup - tStart);

        // Calibration φάση
        if (tNow < baselineSec)
        {
            // αρχικό baseline: μέση τιμή & διασπορά ηρεμίας
            mu0 = MovingAverage(mu0, curMean, 0.1f);
            sigma0 = Mathf.Max(1e-6f, MovingAverage(sigma0, curStd, 0.1f));
            baselineGI.Add(curGI);

            if (statusText) statusText.text = "Calibrating… hold steady";
            UpdateUI();
            return;
        }

        // Αφού τελειώσει το baseline, υπολόγισε adaptive threshold μια φορά
        if (float.IsPositiveInfinity(thGrip))
        {
            thGrip = MedianIQR_Threshold(baselineGI, gripK);
        }

        // Απόφαση: Stable / Unstable
        string decision = (curGI > thGrip) ? "Unstable" : "Stable";

        // UI
        if (statusText) statusText.text = decision;
        UpdateUI();

        // Προαιρετικός περιοδικός summary log (ανά 0.25 s)
        if (Mathf.FloorToInt(tNow * 4) != Mathf.FloorToInt((tNow - Time.deltaTime) * 4))
        {
            summaryLines.Add($"run,{tNow:F2},{curMean:F3},{curStd:F3},{curPeak:F3},{curGI:F3},{decision}");
        }
    }

    void UpdateUI()
    {
        if (meanText) meanText.text = $"mean |B|: {curMean:F2} µT";
        if (stdText) stdText.text = $"std |B|:  {curStd:F2} µT";
        if (peakText) peakText.text = $"ΔB (p2p): {curPeak:F2} µT";
        if (giText) giText.text = $"GripIdx :  {curGI:F2}";
    }

    static void ComputeWindowFeatures(Queue<float> q, out float mean, out float std, out float peak)
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
        foreach (var v in q) var += (v - mean) * (v - mean);
        var /= n;
        std = Mathf.Sqrt(var);
        peak = maxv - minv; // peak-to-peak
    }

    static float GripIndex(float peak, float std, float mean, float mu0)
    {
        // απλός δείκτης: συνδυασμός peak + std, κανονικοποιημένος ως προς baseline mean
        float w1 = 0.6f, w2 = 0.4f;
        float norm = Mathf.Max(5f, mu0); // για να μην εκραγεί αν mu0≈0
        return w1 * peak + w2 * std * (mean / norm);
    }

    static float MovingAverage(float prev, float current, float alpha)
    {
        return alpha * current + (1f - alpha) * prev;
    }

    static float MedianIQR_Threshold(List<float> data, float k)
    {
        if (data == null || data.Count == 0) return 0f;
        var arr = new List<float>(data);
        arr.Sort();
        float median = arr[arr.Count / 2];
        float q1 = arr[(int)(0.25f * (arr.Count - 1))];
        float q3 = arr[(int)(0.75f * (arr.Count - 1))];
        float iqr = Mathf.Max(1e-6f, q3 - q1);
        return median + k * iqr;
    }

    // Κουμπί: σώσε summary CSV για τη δοκιμή
    public void SaveSummary()
    {
        try
        {
            string dir = Application.persistentDataPath + "/mag_summary";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string file = $"{dir}/summary_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            File.WriteAllLines(file, summaryLines);
            Debug.Log("[GripAnalyzer] Saved summary at: " + file);
        }
        catch (Exception e)
        {
            Debug.LogError("[GripAnalyzer] SaveSummary FAILED: " + e.Message);
        }
    }

    // Reset για νέα δοκιμή (π.χ. Stable run → Reset → Unstable run)
    public void ResetRun()
    {
        tStart = Time.realtimeSinceStartup;
        winMag.Clear();
        baselineGI.Clear();
        mu0 = 0f; sigma0 = 1f;
        thGrip = float.PositiveInfinity;
        if (statusText) statusText.text = "Calibrating…";
        Debug.Log("[GripAnalyzer] Reset.");
        // Κράτησε τα παλιά summaryLines αν θέλεις να συγκρίνεις 2 runs στο ίδιο αρχείο.
        // Αλλιώς, άδειασέ τα:
        // summaryLines.Clear(); summaryLines.Add("phase,t_ms,mean,std,peak2peak,GI,decision");
    }
}

