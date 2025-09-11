using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;

public class UiDebugOnce : MonoBehaviour
{
    public GameObject sign; // RoomSign_r000001 인스턴스를 인스펙터에 드래그

    void Start()
    {
        // 0) EventSystem 중복/부재 체크
        var esAll = FindObjectsOfType<EventSystem>(true);
        Debug.Log($"[UI] EventSystem count = {esAll.Length}");
        if (esAll.Length != 1) Debug.LogError("[UI] EventSystem은 씬에 정확히 1개만 있어야 합니다.");

        // 1) 버튼 정확히 집기
        var canvas = sign.GetComponentInChildren<Canvas>(true);
        var t = sign.transform.Find("Canvas/JoinButton");
        var btn = t ? t.GetComponent<Button>() : null;

        Debug.Log($"[UI] canvas={canvas?.name}, btn={btn?.name}, parentCanvas={btn?.GetComponentInParent<Canvas>()?.name}");

        // 2) World Space 세팅 보장
        if (canvas != null)
        {
            canvas.renderMode = RenderMode.WorldSpace;           // 테스트로 월드 스페이스 유지
            canvas.worldCamera = Camera.main;                    // MainCamera 태그 필수
            Debug.Log($"[UI] worldCamera set? {(canvas.worldCamera != null)}");
        }

        // 3) 버튼 상태 강제 + 클릭 로그 핸들러
        if (btn != null)
        {
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => Debug.Log("[UI] JoinButton CLICK"));
            btn.interactable = true;
            btn.gameObject.SetActive(true);

            var img = btn.targetGraphic as Graphic;
            Debug.Log($"[UI] targetGraphic={(img != null)}, raycastTarget={(img != null && img.raycastTarget)}");
        }
    }

    void Update()
    {
        // 4) 지금 마우스 클릭이 UI에 무엇에 맞는지 출력
        if (Input.GetMouseButtonDown(0))
        {
            if (EventSystem.current == null) { Debug.Log("[UI] No EventSystem"); return; }
            var pd = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pd, hits);

            if (hits.Count == 0) Debug.Log("[UI] Raycast: no hits");
            else Debug.Log("[UI] Raycast top: " + hits[0].gameObject.name);
        }
    }
}
