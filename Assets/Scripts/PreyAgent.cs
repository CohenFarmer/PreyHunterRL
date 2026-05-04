using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using System.Collections.Generic;

// arena is optional, null means inference deployment with no training scaffold
public class PreyAgent : Agent, IDamageable
{
    [Header("References")]
    public PreyArenaManager arena;
    public Transform headPivot;

    [Header("Species (0=deer, 1=pig, 2=cow)")]
    public int species = 0;

    [Header("Movement")]
    public float turnSpeed = 200f;
    public float headYawSpeed = 150f;
    public float headPitchSpeed = 60f;
    public float maxHeadYaw = 90f;
    public float maxHeadPitch = 15f;

    [Header("Vision Cone")]
    public float visionRange = 25f;
    public LayerMask visionMask;

    [Header("Smell")]
    public float maxSmellRange = 80f;

    [Header("Biology")]
    public float hungerRate = 0.0001f;
    public float thirstRate = 0.0001f;
    public float hungerReduction = 0.5f;
    public float thirstReduction = 0.5f;
    public float healthRegenRate = 0.001f;
    public float healthRegenThreshold = 0.5f;

    [Header("Rewards")]
    public float driveRewardScale = 50f;
    public float deathPenalty = -10f;
    public float survivalBonus = 10f;

    // deer fast and fragile, pig slow and meaty, cow slow and tanky
    private static readonly float[] speciesSpeeds  = { 9f,  4f,  3f  };
    private static readonly float[] speciesHealth  = { 10f, 20f, 50f };
    private static readonly Vector3[] speciesScales = {
        new Vector3(1.0f, 1.8f, 1.5f),
        new Vector3(1.5f, 1.2f, 2.5f),
        new Vector3(2.0f, 2.0f, 3.0f)
    };

    [HideInInspector] public float moveSpeed;
    [HideInInspector] public float maxHealth;
    [HideInInspector] public float currentHealth;
    [HideInInspector] public float hunger;
    [HideInInspector] public float thirst;
    [HideInInspector] public bool alive;

    public bool IsAlive => alive;

    private float headYaw;
    private float headPitch;
    private List<Vector3> rayDirections = new List<Vector3>();
    private int totalRays;
    private int stepsSurvived;
    private float prevDrive;

    private Rigidbody rb;

    void Awake()
    {
        BuildVisionCone();
        rb = GetComponent<Rigidbody>();
    }

    private void BuildVisionCone()
    {
        rayDirections.Clear();
        rayDirections.Add(Vector3.forward);
        AddRing(3f, 6);
        AddRing(8f, 12);
        AddRing(16f, 16);
        AddRing(25f, 16);
        totalRays = rayDirections.Count;
    }

    private void AddRing(float angleFromCenter, int numRays)
    {
        for (int i = 0; i < numRays; i++)
        {
            float azimuth = (360f / numRays) * i;
            float azimuthRad = azimuth * Mathf.Deg2Rad;
            float angleRad = angleFromCenter * Mathf.Deg2Rad;

            float x = Mathf.Sin(angleRad) * Mathf.Sin(azimuthRad);
            float y = Mathf.Sin(angleRad) * Mathf.Cos(azimuthRad);
            float z = Mathf.Cos(angleRad);

            rayDirections.Add(new Vector3(x, y, z).normalized);
        }
    }

    public override void OnEpisodeBegin()
    {
        species = Mathf.Clamp(species, 0, 2);
        moveSpeed = speciesSpeeds[species];
        maxHealth = speciesHealth[species];
        currentHealth = maxHealth;

        Vector3 scale = speciesScales[species];
        transform.localScale = scale;

        if (arena != null)
        {
            // start with one need high, on the opposite side from its resource
            bool startThirsty = Random.value > 0.5f;
            Vector3 spawnPos;
            if (startThirsty)
            {
                hunger = 0.2f;
                thirst = 0.7f;
                spawnPos = arena.GetSpawnOnFoodSide();
            }
            else
            {
                hunger = 0.7f;
                thirst = 0.2f;
                spawnPos = arena.GetSpawnOnWaterSide();
            }
            spawnPos.y = scale.y / 2f;
            transform.localPosition = spawnPos;
        }
        else
        {
            hunger = 0.2f;
            thirst = 0.2f;
        }

        alive = true;
        headYaw = 0f;
        headPitch = 0f;
        stepsSurvived = 0;
        prevDrive = ComputeDrive();

        transform.localRotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);

        if (headPivot != null)
            headPivot.localRotation = Quaternion.identity;

        if (arena != null) arena.ResetArena();
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // species one hot, 3
        sensor.AddObservation(species == 0 ? 1f : 0f);
        sensor.AddObservation(species == 1 ? 1f : 0f);
        sensor.AddObservation(species == 2 ? 1f : 0f);

        // needs, 3
        sensor.AddObservation(hunger);
        sensor.AddObservation(thirst);
        sensor.AddObservation(currentHealth / maxHealth);

        // own facing, 2
        float yaw = transform.localEulerAngles.y * Mathf.Deg2Rad;
        sensor.AddObservation(Mathf.Sin(yaw));
        sensor.AddObservation(Mathf.Cos(yaw));

        // head direction, 2
        sensor.AddObservation(headYaw / maxHeadYaw);
        sensor.AddObservation(headPitch / maxHeadPitch);

        // vision cone, 102
        CastVisionCone(sensor);

        // 3 + 3 + 2 + 2 + 102 = 112 total
    }

    private void CastVisionCone(VectorSensor sensor)
    {
        Quaternion headRot = headPivot != null ? headPivot.rotation : transform.rotation;
        Vector3 headPos = headPivot != null ? headPivot.position : transform.position + Vector3.up * 0.3f;

        int obstacleLayer = LayerMask.NameToLayer("Obstacle");
        int npcLayer = LayerMask.NameToLayer("NPC");
        int animalLayer = LayerMask.NameToLayer("Animal");
        int foodLayer = LayerMask.NameToLayer("Food");
        int waterLayer = LayerMask.NameToLayer("Water");
        int riverLayer = LayerMask.NameToLayer("River");

        for (int i = 0; i < totalRays; i++)
        {
            Vector3 worldDir = headRot * rayDirections[i];

            if (Physics.Raycast(headPos, worldDir, out RaycastHit hit, visionRange, visionMask))
            {
                sensor.AddObservation(hit.distance / visionRange);

                int hitLayer = hit.collider.gameObject.layer;
                if (hitLayer == obstacleLayer)
                    sensor.AddObservation(0f);
                else if (hitLayer == npcLayer)
                    sensor.AddObservation(0.25f);
                else if (hitLayer == animalLayer)
                    sensor.AddObservation(0.5f);
                else if (hitLayer == foodLayer)
                    sensor.AddObservation(0.75f);
                else if (hitLayer == waterLayer)
                    sensor.AddObservation(1f);
                else if (hitLayer == riverLayer)
                    sensor.AddObservation(-0.25f);
                else
                    sensor.AddObservation(-0.5f);
            }
            else
            {
                sensor.AddObservation(1f);
                sensor.AddObservation(-1f);
            }
        }
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (!alive) return;

        float forwardAction = Mathf.Clamp(actions.ContinuousActions[0], 0f, 1f);
        float turnAction = Mathf.Clamp(actions.ContinuousActions[1], -1f, 1f);
        float headYawAction = Mathf.Clamp(actions.ContinuousActions[2], -1f, 1f);
        float headPitchAction = Mathf.Clamp(actions.ContinuousActions[3], -1f, 1f);

        float dt = Time.fixedDeltaTime;

        transform.Rotate(0, turnAction * turnSpeed * dt, 0);
        Vector3 velocity = transform.forward * forwardAction * moveSpeed;
        velocity.y = 0;
        rb.linearVelocity = velocity;

        headYaw = Mathf.Clamp(headYaw + headYawAction * headYawSpeed * dt, -maxHeadYaw, maxHeadYaw);
        headPitch = Mathf.Clamp(headPitch + headPitchAction * headPitchSpeed * dt, -maxHeadPitch, maxHeadPitch);
        if (headPivot != null)
            headPivot.localRotation = Quaternion.Euler(-headPitch, headYaw, 0);

        hunger += hungerRate;
        thirst += thirstRate;
        hunger = Mathf.Clamp01(hunger);
        thirst = Mathf.Clamp01(thirst);

        stepsSurvived++;

        if (arena != null)
        {
            if (hunger > 0.1f && arena.TryEatBerry(transform.position))
            {
                hunger -= hungerReduction;
                hunger = Mathf.Clamp01(hunger);
            }
            if (thirst > 0.1f && arena.TryDrinkWater(transform.position))
            {
                thirst -= thirstReduction;
                thirst = Mathf.Clamp01(thirst);
            }
        }

        if (hunger < healthRegenThreshold && thirst < healthRegenThreshold)
        {
            currentHealth += healthRegenRate;
            if (currentHealth > maxHealth) currentHealth = maxHealth;
        }

        // hrrl drive reduction
        float currDrive = ComputeDrive();
        float driveReward = (prevDrive - currDrive) * driveRewardScale;
        prevDrive = currDrive;

        AddReward(driveReward);

        if (hunger >= 1f)
        {
            alive = false;
            AddReward(deathPenalty);
            LogAndEnd();
            return;
        }

        if (thirst >= 1f)
        {
            alive = false;
            AddReward(deathPenalty);
            LogAndEnd();
            return;
        }

        if (currentHealth <= 0f)
        {
            alive = false;
            AddReward(deathPenalty);
            LogAndEnd();
            return;
        }

        if (arena != null && arena.IsEpisodeOver())
        {
            AddReward(survivalBonus);
            LogAndEnd();
        }
    }

    private void LogAndEnd()
    {
        Academy.Instance.StatsRecorder.Add("Prey/StepsSurvived", stepsSurvived);
        if (species == 0)
            Academy.Instance.StatsRecorder.Add("Prey/StepsSurvived_Deer", stepsSurvived);
        else if (species == 1)
            Academy.Instance.StatsRecorder.Add("Prey/StepsSurvived_Pig", stepsSurvived);
        else if (species == 2)
            Academy.Instance.StatsRecorder.Add("Prey/StepsSurvived_Cow", stepsSurvived);
        EndEpisode();
    }

    private float ComputeDrive()
    {
        return Mathf.Sqrt(hunger * hunger + thirst * thirst);
    }

    public void TakeDamage(float damage)
    {
        if (!alive) return;
        currentHealth -= damage;
        if (currentHealth <= 0f)
        {
            currentHealth = 0f;
            alive = false;
            AddReward(deathPenalty);
            LogAndEnd();
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!alive) return;
        if (collision.gameObject.layer == LayerMask.NameToLayer("River"))
        {
            alive = false;
            AddReward(deathPenalty);
            LogAndEnd();
        }
    }

    void OnDrawGizmosSelected()
    {
        if (headPivot == null) return;

        Quaternion headRot = headPivot.rotation;
        Vector3 headPos = headPivot.position;

        if (rayDirections == null || rayDirections.Count == 0)
            BuildVisionCone();

        for (int i = 0; i < rayDirections.Count; i++)
        {
            Vector3 worldDir = headRot * rayDirections[i];
            float t = (float)i / rayDirections.Count;
            Gizmos.color = Color.Lerp(Color.cyan, Color.blue, t);
            Gizmos.DrawRay(headPos, worldDir * visionRange);
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var ca = actionsOut.ContinuousActions;
        ca[0] = Input.GetAxis("Vertical");
        ca[1] = Input.GetAxis("Horizontal");
        ca[2] = 0f;
        ca[3] = 0f;
    }
}
