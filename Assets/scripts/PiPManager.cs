using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class PiPManager : MonoBehaviour
{
    [Header("Shared Data")]
    public KeyframeData keyframeData;

    [Header("References")]
    public Camera pipCamera;
    public Camera vrCamera;
    public Transform selectedPlayer;
    public Transform ball;
    public RawImage pipRawImage;

    [Header("PiP Settings")]
    public float pipDuration = 2f;
    public float fadeDuration = 0.3f;
    public float alignThreshold = 15f;
    [Tooltip("Seconds after appearing before angle-check can hide PiP.")]
    public float minDisplayTime = 0.5f;

    // -------------------------------------------------------------------------

    private CanvasGroup _canvasGroup;
    private Coroutine _activeCoroutine;
    private bool _pipActive;
    private Quaternion _recommendedRotation;
    private float _pipShowTime; // when PiP last appeared

    private void Awake()
    {
        if (pipRawImage != null)
            _canvasGroup = pipRawImage.GetComponentInParent<CanvasGroup>();
        SetAlpha(0f);
        if (pipCamera != null) pipCamera.enabled = false;
    }

    private void Update()
    {
        if (!_pipActive) return;

        // PiP Camera position = VR Camera position
        if (pipCamera != null && vrCamera != null)
            pipCamera.transform.position = vrCamera.transform.position;

        // Every frame: recalculate direction toward ball
        if (pipCamera != null && ball != null && vrCamera != null)
        {
            Vector3 dirToBall = (ball.position - vrCamera.transform.position).normalized;
            if (dirToBall != Vector3.zero)
            {
                _recommendedRotation = Quaternion.LookRotation(dirToBall, Vector3.up);
                pipCamera.transform.rotation = _recommendedRotation;
            }
        }

        // Only check angle after minDisplayTime has passed
        if (vrCamera != null && Time.time - _pipShowTime >= minDisplayTime)
        {
            float angle = Quaternion.Angle(vrCamera.transform.rotation, _recommendedRotation);
            if (angle < alignThreshold) HidePiP();
        }
    }

    public void ShowPiP(int keyframeIndex)
    {
        Debug.Log($"[PiP] ShowPiP called, keyframeData={keyframeData}, ball={ball}, vrCamera={vrCamera}, pipCamera={pipCamera}");

        if (keyframeData == null || keyframeIndex >= keyframeData.keyframes.Length)
        {
            Debug.LogWarning("[PiP] keyframeData is null or index out of range, returning");
            return;
        }
        if (pipCamera == null)
        {
            Debug.LogWarning("[PiP] pipCamera is null, returning");
            return;
        }

        // Recommended direction = from player head toward ball
        if (ball != null && vrCamera != null)
        {
            Vector3 dirToBall = (ball.position - vrCamera.transform.position).normalized;
            if (dirToBall != Vector3.zero)
                _recommendedRotation = Quaternion.LookRotation(dirToBall, Vector3.up);
            else
                _recommendedRotation = vrCamera.transform.rotation;
        }
        else
        {
            _recommendedRotation = vrCamera != null ? vrCamera.transform.rotation : Quaternion.identity;
            Debug.LogWarning($"[PiP] ball={ball} vrCamera={vrCamera}, using fallback rotation");
        }

        Debug.Log($"[PiP] recommendedRot={_recommendedRotation.eulerAngles}");

        _pipShowTime = Time.time;
        _pipActive = true;
        if (pipCamera != null) pipCamera.enabled = true; // enable camera when showing
        if (_activeCoroutine != null) StopCoroutine(_activeCoroutine);
        _activeCoroutine = StartCoroutine(PiPSequence());
    }

    public void HidePiP()
    {
        _pipActive = false;
        if (pipCamera != null) pipCamera.enabled = false; // disable camera when hiding
        if (_activeCoroutine != null) StopCoroutine(_activeCoroutine);
        _activeCoroutine = StartCoroutine(FadeCoroutine(GetAlpha(), 0f));
    }

    private IEnumerator PiPSequence()
    {
        yield return FadeCoroutine(0f, 1f);
        yield return new WaitForSeconds(pipDuration);
        HidePiP();
    }

    private IEnumerator FadeCoroutine(float from, float to)
    {
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            SetAlpha(Mathf.Lerp(from, to, elapsed / fadeDuration));
            yield return null;
        }
        SetAlpha(to);
    }

    private void SetAlpha(float a)
    {
        if (_canvasGroup != null) { _canvasGroup.alpha = a; return; }
        if (pipRawImage != null) { var c = pipRawImage.color; c.a = a; pipRawImage.color = c; }
    }

    private float GetAlpha()
    {
        if (_canvasGroup != null) return _canvasGroup.alpha;
        return pipRawImage != null ? pipRawImage.color.a : 0f;
    }
}