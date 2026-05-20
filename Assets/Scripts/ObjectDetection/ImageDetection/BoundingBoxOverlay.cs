using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BoundingBoxOverlay : MonoBehaviour
{
    [SerializeField] Image topBorder;
    [SerializeField] Image bottomBorder;
    [SerializeField] Image leftBorder;
    [SerializeField] Image rightBorder;
    [SerializeField] TextMeshProUGUI label;
    [SerializeField] float borderThickness = 3f;

    RectTransform _rt;
    RectTransform _canvasRt;

    void Awake()
    {
        _rt = GetComponent<RectTransform>();
        _canvasRt = GetComponentInParent<Canvas>().GetComponent<RectTransform>();
    }

    public void SetBox(DetectionResult det, Color color)
    {
        float pixX = det.normalizedRect.x * _canvasRt.rect.width;
        float pixY = det.normalizedRect.y * _canvasRt.rect.height;
        float pixW = det.normalizedRect.width  * _canvasRt.rect.width;
        float pixH = det.normalizedRect.height * _canvasRt.rect.height;

        _rt.anchorMin = Vector2.zero;
        _rt.anchorMax = Vector2.zero;
        _rt.anchoredPosition = new Vector2(pixX, pixY);
        _rt.sizeDelta = new Vector2(pixW, pixH);

        SetBorderRect(topBorder,    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -borderThickness), new Vector2(0f, 0f),             color);
        SetBorderRect(bottomBorder, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f),              new Vector2(0f, borderThickness),  color);
        SetBorderRect(leftBorder,   new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0f),              new Vector2(borderThickness, 0f),  color);
        SetBorderRect(rightBorder,  new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-borderThickness, 0f), new Vector2(0f, 0f),              color);

        label.text  = $"{det.className}  {det.score:P0}";
        label.color = color;
    }

    static void SetBorderRect(Image img, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Color color)
    {
        var rt = img.rectTransform;
        rt.anchorMin   = anchorMin;
        rt.anchorMax   = anchorMax;
        rt.offsetMin   = offsetMin;
        rt.offsetMax   = offsetMax;
        img.color      = color;
    }
}
