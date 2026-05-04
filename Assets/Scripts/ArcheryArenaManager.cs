using UnityEngine;
using Unity.MLAgents;
using System.Collections.Generic;

// hunter trains on a fixed tower, targets spawn in front and bounce side to side
// curriculum varies size, speed, distance, count all together each phase
public class ArcheryArenaManager : MonoBehaviour
{
    [Header("Arena Dimensions")]
    public float arenaWidth = 50f;
    public float arenaDepth = 20f;

    [Header("Target")]
    public GameObject targetPrefab;
    public int maxStepsPerEpisode = 2000;

    [Header("Curriculum")]
    public bool useCurriculum = true;
    public string curriculumParameterName = "archery_phase";
    public int currentPhase = 1;

    private int stepCount;
    private List<GameObject> currentTargets = new List<GameObject>();

    // scale, mvSpeed, zMin, zMax, numTargets
    private static readonly float[,] PhaseTable = new float[,]
    {
        { 2.0f, 0.5f, -3f,  3f,   1f },
        { 2.5f, 1.0f, -5f,  5f,   1f },
        { 3.0f, 1.5f, -7f,  7f,   1f },
        { 3.5f, 2.5f, -9f,  9f,   1f },
        { 3.5f, 3.5f, -9f,  9f,   1f },
        { 3.5f, 4.5f, -9f,  9f,   1f },
        { 3.5f, 5.0f, -9f,  9f,   1f },
        { 3.0f, 5.0f, -9f,  9f,   1f },
        { 2.5f, 5.0f, -9f,  9f,   1f },
        { 2.0f, 5.0f, -9f,  9f,   1f },
        { 2.0f, 6.0f, -9f,  9f,   1f },
        { 2.0f, 7.0f, -9f,  9f,   1f },
        { 2.0f, 6.0f, -9f,  9f,   2f },
        { 1.8f, 6.0f, -9f,  9f,   3f },
        { 1.5f, 7.0f, -9f,  9f,   4f },
    };

    public void ResetArena()
    {
        CancelInvoke();
        stepCount = 0;

        if (useCurriculum)
        {
            float phaseFloat = Academy.Instance.EnvironmentParameters.GetWithDefault(curriculumParameterName, currentPhase);
            int newPhase = Mathf.Clamp(Mathf.RoundToInt(phaseFloat), 1, PhaseTable.GetLength(0));
            if (newPhase != currentPhase)
            {
                Debug.Log($"phase {currentPhase} to {newPhase}");
                currentPhase = newPhase;
            }
        }

        DestroyAllTargets();
        SpawnTargets();
    }

    public void IncrementStep() { stepCount++; }
    public bool IsEpisodeOver() => stepCount >= maxStepsPerEpisode;

    private void DestroyAllTargets()
    {
        foreach (GameObject t in currentTargets)
        {
            if (t != null) Destroy(t);
        }
        currentTargets.Clear();
    }

    private void SpawnTargets()
    {
        if (targetPrefab == null) return;

        int idx = Mathf.Clamp(currentPhase - 1, 0, PhaseTable.GetLength(0) - 1);
        float scale     = PhaseTable[idx, 0];
        float moveSpeed = PhaseTable[idx, 1];
        float zMin      = PhaseTable[idx, 2];
        float zMax      = PhaseTable[idx, 3];
        int numTargets  = (int)PhaseTable[idx, 4];

        float halfWidth = arenaWidth / 2f;

        for (int i = 0; i < numTargets; i++)
        {
            float spawnX = Random.Range(-halfWidth + 1f, halfWidth - 1f);
            float spawnZ = Random.Range(zMin, zMax);
            float spawnY = 0.5f * scale;

            GameObject target = Instantiate(targetPrefab, transform);
            target.transform.localPosition = new Vector3(spawnX, spawnY, spawnZ);
            target.transform.localScale = Vector3.one * scale;

            ArcheryTarget targetScript = target.GetComponent<ArcheryTarget>();
            if (targetScript == null)
                targetScript = target.AddComponent<ArcheryTarget>();

            targetScript.moveSpeed = moveSpeed;
            targetScript.arenaWidth = arenaWidth;
            targetScript.movingRight = Random.value > 0.5f;

            currentTargets.Add(target);
        }
    }

    public void OnTargetKilled()
    {
        DestroyAllTargets();
        Invoke("SpawnTargets", 0.5f);
    }

    public int GetStepCount() => stepCount;
}
