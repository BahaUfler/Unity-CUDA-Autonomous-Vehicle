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

    [Header("LiDAR Ayarları")]
    public int rayCount = 90;
    public float safeDistance = 5.5f;

    [Header("Güzergah Ayarları")]
    public List<Transform> waypoints;
    public float waypointReachDistance = 2.0f;
    private int currentWaypointIndex = 0;

    [Header("Araç Hareket Ayarları")]
    private Rigidbody rb;
    public float maxSpeed = 6f;
    public float rotationSpeed = 300f;

    private int lockedAvoidSign = 0;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            // Sadece X ve Z devrilme eksenleri ile Y yüksekliğini kilitle; Y ekseninde (sağa-sola) serbest dönsün
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ | RigidbodyConstraints.FreezePositionY;
            rb.useGravity = false;
        }

        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            col.isTrigger = true;
        }
    }

    void FixedUpdate()
    {
        // 1. Engelleri Topla (Sadece "Respawn" etiketli ve aracın kendisi olmayan objeler)
        GameObject[] obstacleObjects = GameObject.FindGameObjectsWithTag("Respawn");
        List<Vector2> validObstacles = new List<Vector2>();
        for (int i = 0; i < obstacleObjects.Length; i++)
        {
            if (obstacleObjects[i] == this.gameObject) continue;
            Vector3 pos = obstacleObjects[i].transform.position;
            validObstacles.Add(new Vector2(pos.x, pos.z));
        }

        int count = validObstacles.Count;
        float[] obsX = new float[count];
        float[] obsY = new float[count];
        for (int i = 0; i < count; i++)
        {
            obsX[i] = validObstacles[i].x;
            obsY[i] = validObstacles[i].y;
        }

        float carX = transform.position.x;
        float carY = transform.position.z;
        float[] rayDistances = new float[rayCount];

        try
        {
            if (count > 0)
            {
                RunCudaLiDAR(carX, carY, obsX, obsY, rayDistances, count, rayCount);
            }
            else
            {
                for (int i = 0; i < rayCount; i++) rayDistances[i] = 50f;
            }

            float frontMinDist = float.MaxValue;
            float leftSum = 0f;
            float rightSum = 0f;

            for (int i = 0; i < rayCount; i++)
            {
                float worldAngleDeg = i * (360.0f / rayCount);
                float worldAngleRad = worldAngleDeg * Mathf.Deg2Rad;
                Vector3 worldDir = new Vector3(Mathf.Sin(worldAngleRad), 0f, Mathf.Cos(worldAngleRad));

                // 3 Kademeli LiDAR Görselleştirme:
                // < safeDistance: Kırmızı (Tehlike/Kaçınma) | < 49m: Sarı (Engel Algılandı) | 50m: Yeşil (Açık Yol)
                Color rayColor = Color.green;
                if (rayDistances[i] < safeDistance)
                    rayColor = Color.red;
                else if (rayDistances[i] < 49.0f)
                    rayColor = Color.yellow;

                Debug.DrawRay(transform.position, worldDir * rayDistances[i], rayColor);

                float relativeAngle = Vector3.SignedAngle(transform.forward, worldDir, Vector3.up);

                // Dar ön çarpışma koridoru (-25 ile +25 derece arası)
                if (Mathf.Abs(relativeAngle) <= 25f)
                {
                    if (rayDistances[i] < frontMinDist)
                        frontMinDist = rayDistances[i];
                }
                else if (relativeAngle < -25f && relativeAngle >= -110f)
                {
                    leftSum += rayDistances[i];
                }
                else if (relativeAngle > 25f && relativeAngle <= 110f)
                {
                    rightSum += rayDistances[i];
                }
            }

            // 2. Waypoint Hedef Yönü
            Vector3 targetDirection = transform.forward;
            if (waypoints != null && waypoints.Count > 0)
            {
                Transform targetWaypoint = waypoints[currentWaypointIndex];
                if (targetWaypoint != null)
                {
                    Vector3 targetPosFlat = new Vector3(
                        targetWaypoint.position.x,
                        transform.position.y,
                        targetWaypoint.position.z
                    );

                    // Aktif hedef noktasına mavi çizgi çiz
                    Debug.DrawLine(transform.position, targetPosFlat, Color.blue);

                    if (Vector3.Distance(transform.position, targetPosFlat) < waypointReachDistance)
                    {
                        currentWaypointIndex = (currentWaypointIndex + 1) % waypoints.Count;
                        targetWaypoint = waypoints[currentWaypointIndex];
                        targetPosFlat = new Vector3(targetWaypoint.position.x, transform.position.y, targetWaypoint.position.z);
                    }

                    Vector3 toTarget = targetPosFlat - transform.position;
                    toTarget.y = 0f;
                    if (toTarget.sqrMagnitude > 0.001f)
                        targetDirection = toTarget.normalized;
                }
            }

            // 3. Yönlendirme ve Engel Kaçınma
            Vector3 desiredDir = targetDirection;

            if (frontMinDist < safeDistance)
            {
                if (lockedAvoidSign == 0)
                {
                    lockedAvoidSign = (leftSum >= rightSum) ? -1 : 1;
                }

                float urgency = 1f - Mathf.Clamp01(frontMinDist / safeDistance);
                float avoidAngle = lockedAvoidSign * Mathf.Lerp(35f, 65f, urgency);
                desiredDir = (Quaternion.Euler(0f, avoidAngle, 0f) * targetDirection).normalized;
            }
            else
            {
                lockedAvoidSign = 0;
            }

            Quaternion targetRotation = Quaternion.LookRotation(desiredDir, Vector3.up);

            // Hedefe açı farkı büyükse hızı düşür ki dışarı savrulmasın
            float angleToDesired = Vector3.Angle(transform.forward, desiredDir);
            float turnSpeedFactor = Mathf.Clamp01(1f - (angleToDesired / 90f));
            float currentSpeed = Mathf.Lerp(maxSpeed * 0.2f, maxSpeed, turnSpeedFactor);

            // Doğrudan transform üzerinden döndür ve Rigidbody hızını güncelle
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
                rotationSpeed * Time.fixedDeltaTime
            );

            Vector3 moveVel = transform.forward * currentSpeed;
            rb.linearVelocity = new Vector3(moveVel.x, 0f, moveVel.z);
        }
        catch (System.DllNotFoundException)
        {
            Debug.LogError("CUDA DLL bulunamadı!");
        }
    }

    void OnDrawGizmos()
    {
        if (waypoints == null || waypoints.Count == 0) return;

        Gizmos.color = Color.yellow;
        for (int i = 0; i < waypoints.Count; i++)
        {
            if (waypoints[i] == null) continue;
            Gizmos.DrawSphere(waypoints[i].position, 0.4f);

            Transform next = waypoints[(i + 1) % waypoints.Count];
            if (next != null)
            {
                Gizmos.DrawLine(waypoints[i].position, next.position);
            }
        }
    }
}