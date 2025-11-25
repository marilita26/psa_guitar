using UnityEngine;
using TMPro;
using UnityEngine.UI; // μόνο αν θες να χρωματίζεις background/image

public class GripStrengthLabel : MonoBehaviour
{
    public GripStrengthEstimator estimator;  // σύρε εδώ το GripStrengthEstimator
    public TextMeshProUGUI label;           // σύρε ένα TMP Text από το Canvas
    public Image badge;                     // (προαιρετικό) ένα μικρό Image για χρώμα

    [Header("Thresholds (% of 0..100)")]
    [Range(0, 100)] public float mediumTh = 33f;
    [Range(0, 100)] public float strongTh = 66f;

    void Update()
    {
        if (estimator == null || label == null) return;

        float gs = estimator.GripStrengthPercent; // 0..100
        string msg;
        Color c;

        if (gs < mediumTh) { msg = $"Πίεση: Χαμηλή ({gs:F0}%)"; c = new Color(0.2f, 0.7f, 0.2f); }    // πράσινο
        else if (gs < strongTh) { msg = $"Πίεση: Μέτρια ({gs:F0}%)"; c = new Color(1.0f, 0.7f, 0.2f); }    // πορτοκαλί
        else { msg = $"Πίεση: Δυνατή ({gs:F0}%)"; c = new Color(0.9f, 0.2f, 0.2f); }    // κόκκινο

        label.text = msg;
        label.color = c;
        if (badge) badge.color = c;  // αν έχεις μικρό χρωματιστό badge
    }
}

