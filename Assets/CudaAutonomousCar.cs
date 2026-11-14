using UnityEngine;
using System.Runtime.InteropServices;
using System.Collections.Generic;

public class CudaAutonomousCar : MonoBehaviour
{
    const string DllName = "CudaProjesi";

    [DllImport(DllName)]
    private static extern void RunCudaLiDAR(
        float carX, float carY, 
        float[] h_obsX, float[] h_obsY, 
        float[] h_rayDistances, int obstacleCount, int rayCount
    );

    public int rayCount = 36;
    public float safeDistance = 6.0f; // Frenleme ve kaçışın başladığı mesafe
    
    // Hedef Noktaları (Waypoints) - Sahnede boş objeler oluşturup buraya sürükleyebilirsin
    public List<Transform> waypoints;
    private int currentWaypointIndex = 0;

    private Rigidbody rb;
    public float maxSpeed = 8f;
    public float rotationSpeed = 120f;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true; // Kendi etrafında istenmeyen fiziksel taklaları engellemek için
    }

    void FixedUpdate()
    {
        GameObject[] obstacleObjects = GameObject.FindGameObjectsWithTag("Respawn");
        int count = obstacleObjects.Length;
        
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
            if (count > 0)
            {
                // GPU'dan LiDAR verilerini al
                RunCudaLiDAR(carX, carY, obsX, obsY, rayDistances, count, rayCount);
            }
            else
            {
                // Engel yoksa tüm mesafeleri maksimum varsay
                for (int i = 0; i < rayCount; i++) rayDistances[i] = 50f;
            }

            // Sektör Analizi
            float frontMinDist = float.MaxValue;
            float leftSum = 0f;
            float rightSum = 0f;

            for (int i = 0; i < rayCount; i++)
            {
                float angle = i * (360.0f / rayCount) * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle));
                
                Color rayColor = rayDistances[i] < safeDistance ? Color.red : Color.green;
                Debug.DrawRay(transform.position, dir * rayDistances[i], rayColor);

                // Ön Sektör
                if (i < 3 || i > rayCount - 3)
                {
                    if (rayDistances[i] < frontMinDist)
                        frontMinDist = rayDistances[i];
                }
                // Sol Sektör
                else if (i > 0 && i < rayCount / 2)
                {
                    leftSum += rayDistances[i];
                }
                // Sağ Sektör
                else
                {
                    rightSum += rayDistances[i];
                }
            }

            // --- 1. HEDEF (WAYPOINT) NAVİGASYONU ---
            Vector3 targetDirection = Vector3.forward;
            if (waypoints != null && waypoints.Count > 0)
            {
                Transform targetWaypoint = waypoints[currentWaypointIndex];
                Vector3 targetPosFlat = new Vector3(targetWaypoint.position.x, transform.position.y, targetWaypoint.position.z);
                
                // Hedefe yaklaşıldıysa bir sonraki hedefe geç
                if (Vector3.Distance(transform.position, targetPosFlat) < 3.0f)
                {
                    currentWaypointIndex = (currentWaypointIndex + 1) % waypoints.Count;
                }

                targetDirection = (targetPosFlat - transform.position).normalized;
            }

            // Hedefe dönmek için gerekli baz açı
            Quaternion targetRotation = Quaternion.LookRotation(targetDirection);

            // --- 2. ENGELDEN KAÇINMA (LOKAL REFLEKS) ---
            float currentSpeed = maxSpeed;
            float steeringAdjustment = 0f;

            if (frontMinDist < safeDistance)
            {
                // Tehlike anında dinamik frenleme (mesafe azaldıkça hız düşer)
                currentSpeed = maxSpeed * (frontMinDist / safeDistance);

                // Hangi tarafta boşluk varsa o tarafa kaçış açısı ver
                if (leftSum > rightSum)
                {
                    steeringAdjustment = -45f; // Sola kaçış
                }
                else
                {
                    steeringAdjustment = 45f;  // Sağa kaçış
                }

                // Kaçış yönünü hedefe ekle
                targetRotation *= Quaternion.Euler(0, steeringAdjustment, 0);
            }

            // Hareketin Rigidbody ile pürüzsüz uygulanması (Bug'ları yok eden kısım)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime);
            rb.linearVelocity = transform.forward * currentSpeed;
        }
        catch (System.DllNotFoundException)
        {
            Debug.LogError("CUDA DLL bulunamadı!");
        }
    }
}