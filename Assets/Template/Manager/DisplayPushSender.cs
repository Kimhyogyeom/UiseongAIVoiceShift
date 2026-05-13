using System;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

// 영상 URL을 UDP 브로드캐스트로 같은 LAN의 디스플레이 PC에 전달
public class DisplayPushSender : MonoBehaviour
{
    public static DisplayPushSender Instance { get; private set; }

    [Header("UDP Broadcast")]
    [Tooltip("디스플레이 PC가 수신할 포트")]
    [SerializeField] int port = 8090;
    [Tooltip("브로드캐스트 주소. 같은 서브넷 전체 전달.")]
    [SerializeField] string broadcastAddress = "255.255.255.255";

    UdpClient udp;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        try
        {
            udp = new UdpClient { EnableBroadcast = true };
            Debug.Log($"[PushSender] UDP broadcast ready (target {broadcastAddress}:{port})");
        }
        catch (Exception e)
        {
            Debug.LogError($"[PushSender] UDP 초기화 실패: {e.Message}");
        }
    }

    public void Push(string videoUrl)
    {
        if (udp == null || string.IsNullOrEmpty(videoUrl)) return;
        try
        {
            string msg = "NEWVIDEO:" + videoUrl;
            byte[] data = Encoding.UTF8.GetBytes(msg);
            udp.Send(data, data.Length, broadcastAddress, port);
            Debug.Log($"[PushSender] 브로드캐스트 전송: {videoUrl}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[PushSender] 전송 실패: {e.Message}");
        }
    }

    void OnDestroy()
    {
        udp?.Close();
    }
}
