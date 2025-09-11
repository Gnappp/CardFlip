using UnityEngine;

public class SessionInfo : MonoBehaviour
{
    public static SessionInfo I;
    public string actorName;
    public string token;   
    public string worldHost;
    public int worldUdpPort;

    void Awake()
    {
        if (I != null)
        {
            Destroy(gameObject); 
            return; 
        }
        I = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        
    }

    void Update()
    {
        
    }
}
