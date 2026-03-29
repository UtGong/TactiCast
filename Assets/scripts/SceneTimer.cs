using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;

/// <summary>
/// Stops all animations and visualizations after a set duration.
/// </summary>
public class SceneTimer : MonoBehaviour
{
    [Tooltip("Seconds before everything stops.")]
    public float duration = 22f;

    [Header("References (optional — auto-found if left empty)")]
    public List<Animator>        animators  = new List<Animator>();
    public List<MonoBehaviour>   scripts    = new List<MonoBehaviour>();

    private void Start()
    {
        StartCoroutine(StopAfterDelay());
    }

    private IEnumerator StopAfterDelay()
    {
        yield return new WaitForSeconds(duration);

        // Stop all animators
        if (animators.Count == 0)
        {
            // Auto-find all animators in scene
            var found = FindObjectsOfType<Animator>();
            foreach (var a in found) a.speed = 0f;
        }
        else
        {
            foreach (var a in animators) if (a) a.speed = 0f;
        }

        // Disable all key scripts
        if (PlaybackController.Instance != null)
            PlaybackController.Instance.enabled = false;

        if (FindObjectOfType<PiPManager>() != null)
            FindObjectOfType<PiPManager>().enabled = false;

        if (FindObjectOfType<FieldVisualizationManager>() != null)
            FindObjectOfType<FieldVisualizationManager>().enabled = false;

        if (FindObjectOfType<FormationAreaVisualizer>() != null)
            FindObjectOfType<FormationAreaVisualizer>().enabled = false;

        Debug.Log("[SceneTimer] Scene stopped at " + duration + "s.");
    }
}
