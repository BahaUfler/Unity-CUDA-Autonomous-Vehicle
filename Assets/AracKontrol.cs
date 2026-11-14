using UnityEngine;

public class AracKontrol : MonoBehaviour
{
    public float hiz = 10f;
    public float donusHizi = 150f;

    void Update()
    {
        // İleri ve Geri (W / S veya Yukarı / Aşağı Ok Tuşları)
        float dikey = 0f;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) dikey = 1f;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) dikey = -1f;

        // Sağa ve Sola Dönüş (A / D veya Sağa / Sola Ok Tuşları)
        float yatay = 0f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) yatay = -1f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) yatay = 1f;

        // Hareketi uygula
        transform.Translate(Vector3.forward * dikey * hiz * Time.deltaTime);
        transform.Rotate(Vector3.up * yatay * donusHizi * Time.deltaTime);
    }
}