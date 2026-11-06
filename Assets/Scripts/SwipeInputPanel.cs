using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Attach to Canvas/SwipeInputPanel. The panel must have an Image (or other
/// Graphic) with Raycast Target enabled. It forwards pointer positions to the
/// FootballController on the Ball.
/// </summary>
public class SwipeInputPanel : MonoBehaviour,
    IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [SerializeField] private FootballController footballController;

    private void Awake()
    {
        if (footballController == null)
            footballController = FindFirstObjectByType<FootballController>();

        if (footballController == null)
            Debug.LogError("SwipeInputPanel: FootballController was not found.", this);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (footballController != null)
            footballController.BeginSwipe(eventData.position);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (footballController != null)
            footballController.UpdateSwipe(eventData.position);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (footballController != null)
            footballController.EndSwipe(eventData.position);
    }
}
