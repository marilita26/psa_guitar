using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class EMFlog : MonoBehaviour
{
    [Serializable]
    public class MagSample
    {
        public double t_ms;
        public float bx, by, bz;
        public float magnitude;
    }

    private List<MagSample> samples = new List<MagSample>();
    private float t0;
    private bool logging = false;

    void Start()
    {
        // Ο "μαγνητόμετρος" στο Unity εκτίθεται ως compass.rawVector
        // (ναι, είναι αστείο όνομα, αλλά είναι το raw magnetic field)
        Input.compass.enabled = true;
        Input.location.Start(); // βοηθάει σε ορισμένα devices να "ξυπνήσει" ο αισθητήρας
        t0 = Time.realtimeSinceStartup;
        Debug.Log("[EMFlog] Ready. Call StartLogging().");
    }

    void Update()
    {
        if (!logging) return;

        // διαβάζουμε το μαγνητικό πεδίο ανά άξονα (σε microTesla)
        Vector3 rawB = Input.compass.rawVector; // Bx, By, Bz
        float mag = rawB.magnitude;             // |B|

        var s = new MagSample
        {
            t_ms = (Time.realtimeSinceStartup - t0) * 1000.0,
            bx = rawB.x,
            by = rawB.y,
            bz = rawB.z,
            magnitude = mag
        };

        samples.Add(s);

        // debug κάθε ~200 δείγματα
        if (samples.Count % 200 == 0)
        {
            Debug.Log($"[EMFlog] |B|={mag:F2} µT  raw=({rawB.x:F2},{rawB.y:F2},{rawB.z:F2})");
        }
    }

    // Κουμπί 1: ξεκίνα
    public void StartLogging()
    {
        logging = true;
        Debug.Log("[EMFlog] Logging STARTED.");
    }

    // Κουμπί 2: σταμάτα & σώσε
    public void StopAndSave()
    {
        logging = false;
        Debug.Log("[EMFlog] Logging STOPPED.");
        SaveCSV();
    }

    private void SaveCSV()
    {
        try
        {
            string dir = Application.persistentDataPath + "/magnetometer";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string filePath = $"{dir}/mag_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            using (var sw = new StreamWriter(filePath))
            {
                sw.WriteLine("t_ms,bx,by,bz,magnitude");
                foreach (var s in samples)
                {
                    sw.WriteLine($"{s.t_ms:F1},{s.bx:F4},{s.by:F4},{s.bz:F4},{s.magnitude:F4}");
                }
            }

            Debug.Log("[EMFlog] Saved CSV at: " + filePath);
        }
        catch (Exception e)
        {
            Debug.LogError("[EMFlog] Save FAILED: " + e.Message);
        }
    }
}
