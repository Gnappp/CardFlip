using UnityEngine;

public class RoomCreateButton : MonoBehaviour
{
    public GameObject roomCreatePanel; 

    public void OnClickOpenCreate()
    {
        Debug.LogError("OnClickOpenCreate");
        roomCreatePanel.SetActive(true); 
    }
    
    public void OnClickCreateCancel()
    {
        Debug.LogError("OnClickCreateCancel");
        roomCreatePanel.SetActive(false);
    }
}
