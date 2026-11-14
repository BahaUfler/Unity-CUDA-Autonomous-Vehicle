using UnityEngine;
using System.Runtime.InteropServices;

public class CudaLiDARController : MonoBehaviour
{
    const string DllName = "CudaProjesi";

    [DllImport(DllName)]
    private static extern void RunCudaLiDAR(
        float carX, float carY, 
        float[] h_obsX, float[] h_obsY, 
        float[] h_rayDistances, int obstacleCount, int rayCount
    );

    public int rayCount = 36;
    public float safeDistance = 5.0f; // Engelin algılanmaya başlandığı kritik mesafe
    
    private Rigidbody rb;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    void Update()
    {
        GameObject[] obstacleObjects = GameObject.FindGameObjectsWithTag("Respawn");
        int count = obstacleObjects.Length;
        if (count == 0) return;

        float[] obsX = new float[count];
        float[] obsY = new float[count];

        for (int i = 0; i < count; i++)
        {
            Vector3 pos = obstacleObjects[i].transform.position;
            obsX[i] = pos.x;
            obsY[i] = pos.z;
        }

        float carX = transform.position.x;
        float carY = transform.position.z;

        float[] rayDistances = new float[rayCount];

        try
        {
            // 1. GPU üzerinden LiDAR tarama verilerini al
            RunCudaLiDAR(carX, carY, obsX, obsY, rayDistances, count, rayCount);

            // 2. Işınları görselleştir
            float frontMinDist = float.MaxValue;
            float leftSum = 0f;
            float rightSum = 0f;

            int sectorSize = rayCount / 4; // Çeyrek dilimler

            for (int i = 0; i < rayCount; i++)
            {
                float angle = i * (360.0f / rayCount) * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle));
                
                // Renklendirme: Tehlike varsa kırmızı, güvenli ise yeşil
                Color rayColor = rayDistances[i] < safeDistance ? Color.red : Color.green;
                Debug.DrawRay(transform.position, dir * rayDistances[i], rayColor);

                // Sektör Analizi (Ön, Sol, Sağ)
                // Ön bölge (Örn: son çeyrek ve ilk çeyrek indexleri veya 0 civarı)
                if (i < 4 || i > rayCount - 4)
                {
                    if (rayDistances[i] < frontMinDist)
                        frontMinDist = rayDistances[i];
                }
                // Sol bölge
                else if (i > 0 && i < rayCount / 2)
                {
                    leftSum += rayDistances[i];
                }
                // Sağ bölge
                else
                {
                    rightSum += rayDistances[i];
                }
            }

            // 3. Otonom Sürüş Karar Mekanizması (AI Steering)
            float moveSpeed = 5f;
            float turnSpeed = 100f;
            float steerDirection = 0f;

            // Eğer ön tarafta tehlike varsa kaçış manevrasına başla
            if (frontMinDist < safeDistance)
            {
                // Hangi tarafta daha çok boşluk varsa o tarafa kır
                if (leftSum > rightSum)
                {
                    steerDirection = -1f; // Sola kaç
                }
                else
                {
                    steerDirection = 1f;  // Sağa kaç
                }
            }

            // Hareketi araca uygula (İleri yönlü sabit hız + Otonom direksiyon)
            transform.Translate(Vector3.forward * moveSpeed * Time.deltaTime);
            transform.Rotate(Vector3.up * steerDirection * turnSpeed * Time.deltaTime);
        }
        catch (System.DllNotFoundException)
        {
            Debug.LogError("CUDA DLL bulunamadı!");
        }
    }
}