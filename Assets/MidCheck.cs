using System.IO;
using UnityEngine;

public class MidiCheck : MonoBehaviour
{
    void Start()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "MIDI");

        if (Directory.Exists(path))
        {
            var files = Directory.GetFiles(path, "*.mid");
            Debug.Log("Βρέθηκαν " + files.Length + " MIDI αρχεία.");
            foreach (var f in files)
            {
                Debug.Log(" - " + Path.GetFileName(f));
            }
        }
        else
        {
            Debug.LogError("Δεν βρέθηκε ο φάκελος MIDI!");
        }
    }
}
