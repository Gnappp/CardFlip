using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;

public class UiDebugOnce : MonoBehaviour
{
    public GameObject sign; // RoomSign_r000001 �ν��Ͻ��� �ν����Ϳ� �巡��

    void Start()
    {
        // 0) EventSystem �ߺ�/���� üũ
        var esAll = FindObjectsOfType<EventSystem>(true);
        Debug.Log($"[UI] EventSystem count = {esAll.Length}");
        if (esAll.Length != 1) Debug.LogError("[UI] EventSystem�� ���� ��Ȯ�� 1���� �־�� �մϴ�.");

        // 1) ��ư ��Ȯ�� ����
        var canvas = sign.GetComponentInChildren<Canvas>(true);
        var t = sign.transform.Find("Canvas/JoinButton");
        var btn = t ? t.GetComponent<Button>() : null;

        Debug.Log($"[UI] canvas={canvas?.name}, btn={btn?.name}, parentCanvas={btn?.GetComponentInParent<Canvas>()?.name}");

        // 2) World Space ���� ����
        if (canvas != null)
        {
            canvas.renderMode = RenderMode.WorldSpace;           // �׽�Ʈ�� ���� �����̽� ����
            canvas.worldCamera = Camera.main;                    // MainCamera �±� �ʼ�
            Debug.Log($"[UI] worldCamera set? {(canvas.worldCamera != null)}");
        }

        // 3) ��ư ���� ���� + Ŭ�� �α� �ڵ鷯
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
        // 4) ���� ���콺 Ŭ���� UI�� ������ �´��� ���
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
