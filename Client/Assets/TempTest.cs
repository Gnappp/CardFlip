using UnityEngine;
using UnityEngine.EventSystems;

public class TempTest : MonoBehaviour, IPointerClickHandler, IPointerDownHandler, IPointerUpHandler
{
    public void OnPointerClick(PointerEventData e) { Debug.Log("[Probe] Click " + name); }
    public void OnPointerDown(PointerEventData e) { Debug.Log("[Probe] Down  " + name); }
    public void OnPointerUp(PointerEventData e) { Debug.Log("[Probe] Up    " + name); }
}