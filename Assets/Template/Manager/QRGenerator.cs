using UnityEngine;
using UnityEngine.UI;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

public class QRGenerator : MonoBehaviour
{
    public static QRGenerator Instance { get; private set; }

    [Header("QR Settings")]
    public int textureSize = 512;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// QR 코드를 생성하여 RawImage에 표시
    /// </summary>
    public void ShowQR(string data, RawImage targetImage)
    {
        Texture2D texture = GenerateQRTexture(data);
        if (texture != null)
        {
            targetImage.texture = texture;
            targetImage.gameObject.SetActive(true);
        }
    }

    /// <summary>
    /// QR 코드 Texture2D 생성
    /// </summary>
    public Texture2D GenerateQRTexture(string data)
    {
        QRCodeWriter qrWriter = new QRCodeWriter();
        BitMatrix matrix = qrWriter.encode(data, BarcodeFormat.QR_CODE, textureSize, textureSize);

        Texture2D texture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Point;

        for (int y = 0; y < textureSize; y++)
        {
            for (int x = 0; x < textureSize; x++)
            {
                bool dark = matrix[x, y];
                texture.SetPixel(x, textureSize - 1 - y, dark ? Color.black : Color.white);
            }
        }

        texture.Apply();
        return texture;
    }
}
