using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Directional Arrow HUD — shown at keyframes, guides user toward recommended viewpoint.
/// Arrow sits on the edge of the view, pointing toward the recommended direction.
/// Opacity and scale scale with angular difference.
/// Hides when user is looking close enough to the recommended direction.
/// Special case: when recommended direction is behind the user, shows a "behind" indicator.
///
/// Scene setup:
/// 1. World Space Canvas, child of Main Camera, Z=0.5m, scale ~0.001.
/// 2. Image child with arrow sprite → assign to arrowImage.
/// 3. CanvasGroup on Canvas root → assign to canvasGroup.
/// 4. Assign mainCamera in Inspector.
/// </summary>
public class DirectionalArrowUI : MonoBehaviour
{
    [Header("References")]
    public Camera      mainCamera;
    public Image       arrowImage;
    public CanvasGroup canvasGroup;

    [Header("Angle Thresholds")]
    [Tooltip("Arrow fully visible above this angle difference (degrees).")]
    public float fullVisibleAngle = 45f;
    [Tooltip("Arrow hides below this angle difference (degrees).")]
    public float hideAngle = 15f;
    [Tooltip("If recommended dir is within this angle of directly behind, treat as 'behind' case.")]
    public float behindAngle = 30f;

    [Header("Arrow Position")]
    [Tooltip("Distance from canvas center as fraction of canvas half-size (0-0.9).")]
    [Range(0.1f, 0.9f)]
    public float edgeRadius = 0.75f;

    [Header("Arrow Scale")]
    public float maxScale = 1f;
    public float minScale = 0.5f;

    // -------------------------------------------------------------------------

    private bool          _isActive;
    private Quaternion    _recommendedRotation;
    private RectTransform _arrowRect;
    private RectTransform _canvasRect;

    private void Awake()
    {
        if (arrowImage)  _arrowRect  = arrowImage.GetComponent<RectTransform>();
        if (canvasGroup) _canvasRect = canvasGroup.GetComponent<RectTransform>();
        SetAlpha(0f);
        if (arrowImage) arrowImage.enabled = false;
    }

    private void LateUpdate()
    {
        if (!_isActive || mainCamera == null) return;
        UpdateArrow();
    }

    // -------------------------------------------------------------------------
    // Public API — called by PlaybackController
    // -------------------------------------------------------------------------

    public void ShowArrow(Quaternion worldSpaceRotation)
    {
        _recommendedRotation = worldSpaceRotation;
        _isActive = true;
        if (arrowImage) arrowImage.enabled = true;
    }

    public void HideArrow()
    {
        _isActive = false;
        SetAlpha(0f);
        if (arrowImage) arrowImage.enabled = false;
    }

    // -------------------------------------------------------------------------

    private void UpdateArrow()
    {
        Vector3 recommendedForward = _recommendedRotation * Vector3.forward;
        float   angleDiff = Vector3.Angle(mainCamera.transform.forward, recommendedForward);

        if (angleDiff < hideAngle) { SetAlpha(0f); return; }

        float t = Mathf.InverseLerp(hideAngle, fullVisibleAngle, angleDiff);
        SetAlpha(Mathf.Lerp(0f, 1f, t));

        float scale = Mathf.Lerp(minScale, maxScale, t);
        if (_arrowRect != null) _arrowRect.localScale = Vector3.one * scale;

        if (_arrowRect == null || _canvasRect == null) return;

        // Project recommended direction into camera-local space
        Vector3 localDir  = mainCamera.transform.InverseTransformDirection(recommendedForward);
        Vector2 screenDir = new Vector2(localDir.x, localDir.y);

        // BUG FIX: Handle "behind" case — when localDir.z < 0 and XY components are near zero,
        // screenDir would be zero (no meaningful 2D direction).
        // Detect this and push the arrow to the bottom of the HUD as a "behind you" cue.
        bool isBehind = localDir.z < 0f &&
                        screenDir.magnitude < Mathf.Sin(behindAngle * Mathf.Deg2Rad);

        if (isBehind)
        {
            // Place arrow at bottom-center pointing downward ("turn around")
            screenDir = Vector2.down;
        }
        else
        {
            screenDir = screenDir.normalized;
        }

        float halfW  = _canvasRect.rect.width  * 0.5f;
        float halfH  = _canvasRect.rect.height * 0.5f;
        float radius = Mathf.Min(halfW, halfH) * edgeRadius;

        _arrowRect.anchoredPosition = screenDir * radius;

        // Rotate sprite: arrow points toward recommended direction
        float angle = Mathf.Atan2(screenDir.y, screenDir.x) * Mathf.Rad2Deg - 90f;
        _arrowRect.localRotation = Quaternion.Euler(0f, 0f, angle);
    }

    private void SetAlpha(float a)
    {
        if (canvasGroup != null) canvasGroup.alpha = a;
    }
}
