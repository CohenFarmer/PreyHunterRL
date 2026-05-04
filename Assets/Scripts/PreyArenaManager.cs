using UnityEngine;
using Unity.MLAgents;
using System.Collections.Generic;

public class PreyArenaManager : MonoBehaviour
{
    [Header("Arena Dimensions")]
    public float arenaWidth = 50f;
    public float arenaDepth = 20f;
    public float wallHeight = 4f;
    public float wallThickness = 0.5f;
    public float southWallHeight = 1.5f;
    public float riverWidth = 5f;

    [Header("Curriculum")]
    public bool useCurriculum = false;
    public string curriculumParameterName = "arena_width";

    [Header("Food & Water Placement")]
    public float resourceStripWidth = 6f;
    public int numBerryBushes = 4;
    public int numWaterSources = 4;
    public float berryRadius = 2.5f;
    public float waterRadius = 2.5f;

    [Header("Episode")]
    public int maxStepsPerEpisode = 20000;

    [HideInInspector] public List<Transform> berryPositions = new List<Transform>();
    [HideInInspector] public List<Transform> waterPositions = new List<Transform>();

    private int stepCount;
    private bool arenaBuilt = false;

    [HideInInspector] public float bottomZ;
    [HideInInspector] public float topZ;

    void Start()
    {
        if (useCurriculum)
        {
            arenaWidth = Academy.Instance.EnvironmentParameters.GetWithDefault(curriculumParameterName, arenaWidth);
        }
        CalculateLayout();
        if (!arenaBuilt)
            BuildArena();
    }

    private void CalculateLayout()
    {
        bottomZ = -arenaDepth / 2f;
        topZ = arenaDepth / 2f;
    }

    public void ResetArena()
    {
        stepCount = 0;

        if (useCurriculum)
        {
            float currentTargetWidth = Academy.Instance.EnvironmentParameters.GetWithDefault(curriculumParameterName, arenaWidth);
            if (Mathf.Abs(currentTargetWidth - arenaWidth) > 0.01f)
            {
                arenaWidth = currentTargetWidth;
                RebuildArena();
            }
        }
    }

    private void RebuildArena()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child.GetComponent<PreyAgent>() != null) continue;
            Destroy(child.gameObject);
        }
        berryPositions.Clear();
        waterPositions.Clear();
        arenaBuilt = false;
        CalculateLayout();
        BuildArena();
    }

    void FixedUpdate()
    {
        stepCount++;
    }

    public int GetStepCount() => stepCount;
    public bool IsEpisodeOver() => stepCount >= maxStepsPerEpisode;

    public bool TryEatBerry(Vector3 position)
    {
        for (int i = 0; i < berryPositions.Count; i++)
        {
            float dist = Vector3.Distance(position, berryPositions[i].position);
            if (dist < berryRadius)
                return true;
        }
        return false;
    }

    public bool TryDrinkWater(Vector3 position)
    {
        for (int i = 0; i < waterPositions.Count; i++)
        {
            float dist = Vector3.Distance(position, waterPositions[i].position);
            if (dist < waterRadius)
                return true;
        }
        return false;
    }

    public Vector3 GetNearestFoodPosition(Vector3 from)
    {
        float bestDist = float.MaxValue;
        Vector3 bestPos = from;
        for (int i = 0; i < berryPositions.Count; i++)
        {
            float dist = Vector3.Distance(from, berryPositions[i].position);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestPos = berryPositions[i].position;
            }
        }
        return bestPos;
    }

    public Vector3 GetNearestWaterPosition(Vector3 from)
    {
        float bestDist = float.MaxValue;
        Vector3 bestPos = from;
        for (int i = 0; i < waterPositions.Count; i++)
        {
            float dist = Vector3.Distance(from, waterPositions[i].position);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestPos = waterPositions[i].position;
            }
        }
        return bestPos;
    }

    public Vector3 GetRandomSpawnPosition()
    {
        float halfWidth = arenaWidth / 2f - 1f;
        float halfDepth = arenaDepth / 2f - 1f;
        return new Vector3(
            Random.Range(-halfWidth, halfWidth),
            0.5f,
            Random.Range(-halfDepth, halfDepth));
    }

    public Vector3 GetSpawnOnFoodSide()
    {
        float halfWidth = arenaWidth / 2f;
        float halfDepth = arenaDepth / 2f - 1f;
        return new Vector3(
            Random.Range(-halfWidth + 1f, -halfWidth + resourceStripWidth),
            0.5f,
            Random.Range(-halfDepth, halfDepth));
    }

    public Vector3 GetSpawnOnWaterSide()
    {
        float halfWidth = arenaWidth / 2f;
        float halfDepth = arenaDepth / 2f - 1f;
        return new Vector3(
            Random.Range(halfWidth - resourceStripWidth, halfWidth - 1f),
            0.5f,
            Random.Range(-halfDepth, halfDepth));
    }

    public Vector3 ClampToArena(Vector3 pos)
    {
        float halfWidth = arenaWidth / 2f - 0.5f;
        float halfDepth = arenaDepth / 2f - 0.5f;
        pos.x = Mathf.Clamp(pos.x, -halfWidth, halfWidth);
        pos.z = Mathf.Clamp(pos.z, -halfDepth, halfDepth);
        return pos;
    }

    private void BuildArena()
    {
        arenaBuilt = true;
        CalculateLayout();

        float halfWidth = arenaWidth / 2f;
        float halfDepth = arenaDepth / 2f;

        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.transform.SetParent(transform);
        floor.transform.localPosition = new Vector3(0, -0.05f, 0);
        floor.transform.localScale = new Vector3(arenaWidth, 0.1f, arenaDepth);
        floor.name = "Floor";

        Renderer floorRend = floor.GetComponent<Renderer>();
        if (floorRend != null)
            floorRend.material.color = new Color(0.3f, 0.6f, 0.2f);

        CreateWall("WallTop", new Vector3(0, wallHeight / 2f, halfDepth), new Vector3(arenaWidth + wallThickness, wallHeight, wallThickness));
        CreateWall("WallLeft", new Vector3(-halfWidth, wallHeight / 2f, 0), new Vector3(wallThickness, wallHeight, arenaDepth));
        CreateWall("WallRight", new Vector3(halfWidth, wallHeight / 2f, 0), new Vector3(wallThickness, wallHeight, arenaDepth));

        CreateRiver(new Vector3(0, 0.05f, -halfDepth - (riverWidth / 2f)),
                    new Vector3(arenaWidth + wallThickness, 0.1f, riverWidth));

        float hunterPlatformZ = -halfDepth - riverWidth - 6f;
        CreateHunterPlatform(new Vector3(0, 2f, hunterPlatformZ),
                             new Vector3(2f, 4f, 2f));

        SpawnBerryBushes();
        SpawnWaterSources();
    }

    private void CreateWall(string name, Vector3 localPos, Vector3 scale)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.SetParent(transform);
        wall.transform.localPosition = localPos;
        wall.transform.localScale = scale;
        wall.layer = LayerMask.NameToLayer("Obstacle");
        wall.name = name;

        Renderer rend = wall.GetComponent<Renderer>();
        if (rend != null)
            rend.material.color = new Color(0.5f, 0.5f, 0.5f);
    }

    private void CreateRiver(Vector3 localPos, Vector3 scale)
    {
        GameObject river = GameObject.CreatePrimitive(PrimitiveType.Cube);
        river.transform.SetParent(transform);
        river.transform.localPosition = localPos;
        river.transform.localScale = scale;
        int riverLayer = LayerMask.NameToLayer("River");
        river.layer = riverLayer != -1 ? riverLayer : 0;
        river.name = "River";

        Renderer rend = river.GetComponent<Renderer>();
        if (rend != null)
            rend.material.color = new Color(0.2f, 0.4f, 0.85f);
    }

    private void CreateHunterPlatform(Vector3 localPos, Vector3 scale)
    {
        GameObject platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
        platform.transform.SetParent(transform);
        platform.transform.localPosition = localPos;
        platform.transform.localScale = scale;
        platform.layer = LayerMask.NameToLayer("Obstacle");
        platform.name = "HunterPlatform";

        Renderer rend = platform.GetComponent<Renderer>();
        if (rend != null)
            rend.material.color = new Color(0.4f, 0.3f, 0.2f);
    }

    private void SpawnBerryBushes()
    {
        berryPositions.Clear();

        float halfWidth = arenaWidth / 2f;
        float leftMin = -halfWidth + 1.5f;
        float leftMax = -halfWidth + resourceStripWidth;

        float zMin = bottomZ + 3f;
        float zMax = topZ - 3f;
        float spacing = (zMax - zMin) / numBerryBushes;

        for (int i = 0; i < numBerryBushes; i++)
        {
            float x = Random.Range(leftMin, leftMax);
            float z = zMin + spacing * (i + 0.5f);

            GameObject bush = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bush.transform.SetParent(transform);
            bush.transform.localPosition = new Vector3(x, 0.75f, z);
            bush.transform.localScale = Vector3.one * 1.5f;
            bush.name = "BerryBush";
            bush.layer = LayerMask.NameToLayer("Food");

            Collider col = bush.GetComponent<Collider>();
            if (col != null) col.isTrigger = true;

            Renderer rend = bush.GetComponent<Renderer>();
            if (rend != null)
                rend.material.color = new Color(0.1f, 0.7f, 0.1f);

            berryPositions.Add(bush.transform);
        }
    }

    private void SpawnWaterSources()
    {
        waterPositions.Clear();

        float halfWidth = arenaWidth / 2f;
        float rightMin = halfWidth - resourceStripWidth;
        float rightMax = halfWidth - 1.5f;

        float zMin = bottomZ + 3f;
        float zMax = topZ - 3f;
        float spacing = (zMax - zMin) / numWaterSources;

        for (int i = 0; i < numWaterSources; i++)
        {
            float x = Random.Range(rightMin, rightMax);
            float z = zMin + spacing * (i + 0.5f);

            GameObject water = GameObject.CreatePrimitive(PrimitiveType.Cube);
            water.transform.SetParent(transform);
            water.transform.localPosition = new Vector3(x, 0.1f, z);
            water.transform.localScale = new Vector3(3f, 0.2f, 3f);
            water.name = "WaterSource";
            water.layer = LayerMask.NameToLayer("Water");

            Collider col = water.GetComponent<Collider>();
            if (col != null) col.isTrigger = true;

            Renderer rend = water.GetComponent<Renderer>();
            if (rend != null)
                rend.material.color = new Color(0.1f, 0.3f, 0.9f);

            waterPositions.Add(water.transform);
        }
    }
}
