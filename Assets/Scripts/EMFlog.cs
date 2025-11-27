using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class EMFlog : MonoBehaviour
{
    // ----------------- RAW SAMPLE -----------------
    [Serializable]
    public class MagSample
    {
        public double t_ms;
        public float bx, by, bz;
        public float magnitude;
    }

    // ----------------- SESSION META (μία καταγραφή Start→Stop) -----------------
    [Serializable]
    public class SessionMeta
    {
        public string id;          // π.χ. "20251126_215030"
        public string datetime;    // ISO 8601
        public string csvPath;     // πλήρες path προς το CSV
        public int sampleCount;    // πόσα samples γράφτηκαν
        public float meanB;        // μέσο |B|
        public float minB;         // ελάχιστο |B|
        public float maxB;         // μέγιστο |B|
    }

    // ----------------- "ΒΑΣΗ ΔΕΔΟΜΕΝΩΝ" JSON -----------------
    [Serializable]
    public class SessionDB
    {
        public List<SessionMeta> sessions = new List<SessionMeta>();
    }

    // ----------------- INTERNAL STATE -----------------
    private List<MagSample> samples = new List<MagSample>();
    private float t0;
    private bool logging = false;

    // ----------------- UNITY LIFECYCLE -----------------
    void Start()
    {
        // Ο μαγνητόμετρος στο Unity εκτίθεται ως Input.compass.rawVector
        Input.compass.enabled = true;
        Input.location.Start();  // σε αρκετά κινητά "ξυπνάει" τον αισθητήρα

        t0 = Time.realtimeSinceStartup;
        Debug.Log("[EMFlog] Ready. Call StartLogging().");
    }

    void Update()
    {
        if (!logging) return;

        // Διαβάζουμε το μαγνητικό πεδίο ανά άξονα (σε microTesla)
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

        // Debug κάθε ~200 δείγματα
        if (samples.Count % 200 == 0)
        {
            Debug.Log($"[EMFlog] |B|={mag:F2} µT  raw=({rawB.x:F2},{rawB.y:F2},{rawB.z:F2})");
        }
    }

    // ----------------- PUBLIC API ΓΙΑ ΚΟΥΜΠΙΑ -----------------

    // Κουμπί 1: START
    public void StartLogging()
    {
        samples.Clear(); // κάθε run ξεκινάει από άδειο buffer
        t0 = Time.realtimeSinceStartup;
        logging = true;

        Debug.Log("[EMFlog] Logging STARTED.");
    }

    public void StopAndSave()
    {
        if (!logging)
        {
            Debug.LogWarning("[EMFlog] StopAndSave called but logging = false. Ignoring.");
            return;
        }

        logging = false;
        Debug.Log("[EMFlog] Logging STOPPED.");

        string csvPath = SaveCSV();
        SaveToJsonDB(csvPath);

        // Ξεφορτώσου τα παλιά δείγματα για την επόμενη συνεδρία
        samples.Clear();
    }


    // ----------------- CSV SAVE -----------------
    // Σώζει τα raw samples σε CSV και επιστρέφει το path του αρχείου
    private string SaveCSV()
    {
        string filePath = "";
        try
        {
            string dir = Application.persistentDataPath + "/magnetometer";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string id = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            filePath = $"{dir}/mag_{id}.csv";

            using (var sw = new StreamWriter(filePath))
            {
                sw.WriteLine("t_ms;bx;by;bz;magnitude");

                foreach (var s in samples)
                {
                    sw.WriteLine($"{s.t_ms:F1};{s.bx:F4};{s.by:F4};{s.bz:F4};{s.magnitude:F4}");

                }
            }

            Debug.Log("[EMFlog] Saved CSV at: " + filePath);
        }
        catch (Exception e)
        {
            Debug.LogError("[EMFlog] Save FAILED: " + e.Message);
        }

        return filePath;
    }

    // ----------------- JSON "DATABASE" UPDATE -----------------
    // Ενημερώνει / δημιουργεί το emf_sessions_db.json με μια νέα εγγραφή
    private void SaveToJsonDB(string csvPath)
    {
        try
        {
            if (string.IsNullOrEmpty(csvPath))
            {
                Debug.LogWarning("[EMFlog] JSON DB: csvPath is empty, skipping.");
                return;
            }

            int n = samples.Count;
            if (n == 0)
            {
                Debug.LogWarning("[EMFlog] JSON DB: no samples, skipping.");
                return;
            }

            // Βασικά στατιστικά για το τρέχον run
            float sum = 0f;
            float minB = float.MaxValue;
            float maxB = float.MinValue;

            foreach (var s in samples)
            {
                float B = s.magnitude;
                sum += B;
                if (B < minB) minB = B;
                if (B > maxB) maxB = B;
            }

            float meanB = sum / n;

            string id = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var meta = new SessionMeta
            {
                id = id,
                datetime = DateTime.Now.ToString("o"), // ISO 8601
                csvPath = csvPath,
                sampleCount = n,
                meanB = meanB,
                minB = minB,
                maxB = maxB
            };

            // Path για το JSON "database"
            string dbDir = Application.persistentDataPath;
            string dbPath = Path.Combine(dbDir, "emf_sessions_db.json");

            SessionDB db;

            if (File.Exists(dbPath))
            {
                string jsonOld = File.ReadAllText(dbPath);

                // Αν για κάποιο λόγο είναι άδειο/χαλασμένο, πιάστο με try
                try
                {
                    db = JsonUtility.FromJson<SessionDB>(jsonOld);
                    if (db == null) db = new SessionDB();
                }
                catch
                {
                    Debug.LogWarning("[EMFlog] JSON DB corrupted, recreating.");
                    db = new SessionDB();
                }
            }
            else
            {
                db = new SessionDB();
            }

            // Πρόσθεσε τη νέα συνεδρία
            db.sessions.Add(meta);

            // Ξαναγράψε το JSON (pretty-print)
            string jsonNew = JsonUtility.ToJson(db, true);
            File.WriteAllText(dbPath, jsonNew);

            Debug.Log("[EMFlog] JSON DB updated at: " + dbPath);
        }
        catch (Exception e)
        {
            Debug.LogError("[EMFlog] SaveToJsonDB FAILED: " + e.Message);
        }
    }
}
