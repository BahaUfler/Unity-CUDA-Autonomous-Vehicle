#include "cuda_runtime.h"
#include "device_launch_parameters.h"
#include <stdio.h>
#include <math.h>

#define PI 3.1415926535f

// KERNEL: Her bir thread, kendi açısındaki ışın vektörü ile engellerin kesişim mesafesini hesaplar
__global__ void lidarScanKernel(
    float carX, float carY,
    const float* obstacleX, const float* obstacleY,
    float* rayDistances, int obstacleCount, int rayCount, float maxRange
) {
    int rayIdx = blockIdx.x * blockDim.x + threadIdx.x;

    if (rayIdx < rayCount) {
        float angle = (float)rayIdx * (360.0f / (float)rayCount) * (PI / 180.0f);

        // Unity dünya koordinatlarına uyumlu birim yön vektörü (0 derece = +Z, 90 derece = +X)
        float dirX = sinf(angle);
        float dirY = cosf(angle);

        float minObsDistance = maxRange;
        float obstacleRadius = 1.2f; // Sahnede kullanılan Cube engellerinin yaklaşık yarıçapı

        for (int i = 0; i < obstacleCount; i++) {
            float dx = obstacleX[i] - carX;
            float dy = obstacleY[i] - carY;

            // Engelin ışın doğrultusu üzerindeki izdüşümü (t)
            float proj = dx * dirX + dy * dirY;

            // Engel ışının arkasındaysa (proj <= 0) bu ışın çarpmaz
            if (proj > 0.0f) {
                // Engel merkezinin ışın doğrusuna olan dik uzaklığının karesi
                float perpDistSq = (dx * dx + dy * dy) - (proj * proj);
                float radiusSq = obstacleRadius * obstacleRadius;

                // Işın engelin yarıçapı içinden geçiyorsa kesişim mesafesini hesapla
                if (perpDistSq <= radiusSq) {
                    float hitDist = proj - sqrtf(radiusSq - perpDistSq);
                    if (hitDist >= 0.0f && hitDist < minObsDistance) {
                        minObsDistance = hitDist;
                    }
                }
            }
        }

        rayDistances[rayIdx] = minObsDistance;
    }
}

// DLL KÖPRÜSÜ
extern "C" __declspec(dllexport) void RunCudaLiDAR(
    float carX, float carY,
    const float* h_obsX, const float* h_obsY,
    float* h_rayDistances, int obstacleCount, int rayCount
) {
    float* d_obsX = nullptr;
    float* d_obsY = nullptr;
    float* d_rayDistances = nullptr;

    size_t obsSize = obstacleCount * sizeof(float);
    size_t raySize = rayCount * sizeof(float);

    cudaMalloc((void**)&d_obsX, obsSize);
    cudaMalloc((void**)&d_obsY, obsSize);
    cudaMalloc((void**)&d_rayDistances, raySize);

    cudaMemcpy(d_obsX, h_obsX, obsSize, cudaMemcpyHostToDevice);
    cudaMemcpy(d_obsY, h_obsY, obsSize, cudaMemcpyHostToDevice);

    int threadsPerBlock = 256;
    int blocksPerGrid = (rayCount + threadsPerBlock - 1) / threadsPerBlock;

    float maxRange = 50.0f;

    lidarScanKernel << <blocksPerGrid, threadsPerBlock >> > (
        carX, carY, d_obsX, d_obsY, d_rayDistances, obstacleCount, rayCount, maxRange
        );

    cudaMemcpy(h_rayDistances, d_rayDistances, raySize, cudaMemcpyDeviceToHost);

    cudaFree(d_obsX);
    cudaFree(d_obsY);
    cudaFree(d_rayDistances);
}