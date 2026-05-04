using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using System.Collections.Generic;

// arena is optional, null means inference deployment with no training scaffold
public class ArcheryHunterAgent : Agent
{
    [Header("References")]
    public ArcheryArenaManager arena;
    public GameObject arrowPrefab;

    [Header("Procedural Body Parts")]
    public Transform headPivot;
    public Transform bowTransform;
    public Transform drawHand;
    public Transform stringTop;
    public Transform stringBottom;
    public LineRenderer bowString;

    [Header("IK Draw Positions")]
    public Vector3 drawRestPos = new Vector3(0.4f, 0.3f, 0.5f);
    public Vector3 drawFullPos = new Vector3(0.4f, 0.3f, 0f);

    [Header("Body Control")]
    public float bodyYawSpeed = 120f;
    public float headYawSpeed = 150f;
    public float headPitchSpeed = 60f;
    public float maxHeadYaw = 80f;
    public float maxHeadPitch = 15f;
    public float bowPitchSpeed = 45f;
    public float maxBowPitch = 45f;

    [Header("Arrow")]
    public float baseMaxArrowSpeed = 20f;
    public float drawSpeed = 2f;
    public float postFireCooldown = 0.5f;

    [Header("Vision Cone")]
    public float visionRange = 30f;
    public LayerMask visionMask;

    [Header("Stats")]
    public float strength = 0.8f;
    public float dexterity = 0.8f;

    [Header("Combat Config")]
    public int maxArrows = 9999;
    public int maxStepsPerEpisode = 2000;

    [Header("Rewards")]
    public float killReward = 10f;
    public float deathPenalty = -10f;
    public float stepPenalty = -0.001f;

    private float bodyYaw;
    private float headYaw;
    private float headPitch;
    private float bowPitch;
    private float drawAmount;
    private bool arrowInFlight;
    private float fireTimer;
    private int arrowsRemaining;
    private int killCount;

    private List<Vector3> rayDirections = new List<Vector3>();
    private int totalRays;

    void Awake()
    {
        BuildVisionCone();
    }

    private void BuildVisionCone()
    {
        rayDirections.Clear();
        rayDirections.Add(Vector3.forward);
        AddRing(2f, 4);
        AddRing(3f, 6);
        AddRing(4f, 8);
        AddRing(7f, 8);
        AddRing(10f, 12);
        AddRing(15f, 16);
        AddRing(25f, 24);
        totalRays = rayDirections.Count;
    }

    private void AddRing(float angleDeg, int count)
    {
        float angleRad = angleDeg * Mathf.Deg2Rad;
        for (int i = 0; i < count; i++)
        {
            float azimuth = (360f / count) * i;
            float azimuthRad = azimuth * Mathf.Deg2Rad;

            float x = Mathf.Sin(angleRad) * Mathf.Sin(azimuthRad);
            float y = Mathf.Sin(angleRad) * Mathf.Cos(azimuthRad);
            float z = Mathf.Cos(angleRad);

            rayDirections.Add(new Vector3(x, y, z).normalized);
        }
    }

    public override void OnEpisodeBegin()
    {
        bodyYaw = 0f;
        headYaw = 0f;
        headPitch = 0f;
        bowPitch = 0f;
        drawAmount = 0f;
        arrowInFlight = false;
        fireTimer = 0f;
        arrowsRemaining = maxArrows;
        killCount = 0;

        transform.localRotation = Quaternion.Euler(0, 0, 0);
        if (headPivot != null)
            headPivot.localRotation = Quaternion.identity;
        if (bowTransform != null)
            bowTransform.localRotation = Quaternion.identity;

        UpdateDrawHand();

        if (arena != null) arena.ResetArena();
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // body state, 5
        sensor.AddObservation(bodyYaw / 90f);
        sensor.AddObservation(headYaw / maxHeadYaw);
        sensor.AddObservation(headPitch / maxHeadPitch);
        sensor.AddObservation(bowPitch / maxBowPitch);
        sensor.AddObservation(drawAmount);

        // combat state, 3
        sensor.AddObservation(arrowInFlight ? 1f : 0f);
        sensor.AddObservation(arrowsRemaining / (float)maxArrows);
        sensor.AddObservation(fireTimer > 0f ? 1f : 0f);

        // stats, 2
        sensor.AddObservation(strength);
        sensor.AddObservation(dexterity);

        // vision cone, totalRays * 2 = 102
        CastVisionCone(sensor);

        // 10 + 102 = 112 total
    }

    private void CastVisionCone(VectorSensor sensor)
    {
        Quaternion headRot = headPivot != null ? headPivot.rotation : transform.rotation;
        Vector3 headPos = headPivot != null ? headPivot.position : transform.position + Vector3.up * 0.5f;

        int targetLayer = LayerMask.NameToLayer("Animal");
        int obstacleLayer = LayerMask.NameToLayer("Obstacle");

        for (int i = 0; i < totalRays; i++)
        {
            Vector3 worldDir = headRot * rayDirections[i];

            if (Physics.Raycast(headPos, worldDir, out RaycastHit hit, visionRange, visionMask))
            {
                sensor.AddObservation(hit.distance / visionRange);

                if (hit.collider.gameObject.layer == targetLayer)
                    sensor.AddObservation(1f);
                else if (hit.collider.gameObject.layer == obstacleLayer)
                    sensor.AddObservation(0f);
                else
                    sensor.AddObservation(0.5f);
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
        float bodyYawAction = Mathf.Clamp(actions.ContinuousActions[0], -1f, 1f);
        float headYawAction = Mathf.Clamp(actions.ContinuousActions[1], -1f, 1f);
        float headPitchAction = Mathf.Clamp(actions.ContinuousActions[2], -1f, 1f);
        float bowPitchAction = Mathf.Clamp(actions.ContinuousActions[3], -1f, 1f);
        float drawAction = Mathf.Clamp(actions.ContinuousActions[4], 0f, 1f);

        int releaseAction = actions.DiscreteActions[0];

        float dt = Time.fixedDeltaTime;

        bodyYaw += bodyYawAction * bodyYawSpeed * dt;
        bodyYaw = Mathf.Clamp(bodyYaw, -90f, 90f);
        transform.localRotation = Quaternion.Euler(0, bodyYaw, 0);

        headYaw = Mathf.Clamp(headYaw + headYawAction * headYawSpeed * dt, -maxHeadYaw, maxHeadYaw);
        headPitch = Mathf.Clamp(headPitch + headPitchAction * headPitchSpeed * dt, -maxHeadPitch, maxHeadPitch);
        if (headPivot != null)
            headPivot.localRotation = Quaternion.Euler(-headPitch, headYaw, 0);

        bowPitch = Mathf.Clamp(bowPitch + bowPitchAction * bowPitchSpeed * dt, -maxBowPitch, maxBowPitch);
        if (bowTransform != null)
            bowTransform.localRotation = Quaternion.Euler(-bowPitch, 0, 0);

        float effectiveDrawSpeed = drawSpeed * (0.5f + strength * 0.8f);
        if (drawAction > 0.1f)
            drawAmount = Mathf.Clamp01(drawAmount + drawAction * effectiveDrawSpeed * dt);
        else
            drawAmount = Mathf.Clamp01(drawAmount - 0.3f * dt);

        UpdateDrawHand();

        fireTimer -= dt;
        if (releaseAction == 1 && drawAmount > 0.05f && !arrowInFlight && fireTimer <= 0f && arrowsRemaining > 0)
        {
            Fire(drawAmount);
            drawAmount = 0f;
            UpdateDrawHand();
        }

        AddReward(stepPenalty);

        if (arena != null)
        {
            arena.IncrementStep();
            if (arena.IsEpisodeOver())
            {
                AddReward(deathPenalty * 0.3f);
                EndEpisode();
            }
        }
    }

    private void Fire(float drawStrength)
    {
        arrowInFlight = true;
        fireTimer = postFireCooldown;
        arrowsRemaining--;

        Vector3 spawnPos = bowTransform != null ?
            bowTransform.position + transform.forward * 0.3f :
            transform.position + transform.forward * 0.6f + Vector3.up * 0.3f;

        Quaternion fireRotation = Quaternion.Euler(-bowPitch, bodyYaw, 0);

        float spread = (1f - dexterity) * 8f;
        float spreadX = Random.Range(-spread, spread);
        float spreadY = Random.Range(-spread, spread);
        Quaternion spreadRot = Quaternion.Euler(spreadY, spreadX, 0);

        Vector3 fireDir = spreadRot * (fireRotation * Vector3.forward);

        float arrowSpeed = drawStrength * baseMaxArrowSpeed * (0.5f + strength * 0.7f);

        GameObject arrow = Instantiate(arrowPrefab,
            spawnPos,
            Quaternion.LookRotation(fireDir));

        Rigidbody rb = arrow.GetComponent<Rigidbody>();
        rb.linearVelocity = fireDir * arrowSpeed;
        rb.useGravity = true;

        Collider arrowCol = arrow.GetComponent<Collider>();
        Collider myCol = GetComponent<Collider>();
        if (arrowCol != null && myCol != null)
            Physics.IgnoreCollision(arrowCol, myCol);

        ArcheryArrow arrowScript = arrow.GetComponent<ArcheryArrow>();
        if (arrowScript != null)
        {
            arrowScript.owner = this;
            arrowScript.damage = 10f;
        }

        Destroy(arrow, 5f);
    }

    public void OnArrowHit()
    {
        killCount++;
        AddReward(killReward);
        arrowInFlight = false;
        if (arena != null) arena.OnTargetKilled();
        CheckOutOfArrows();
    }

    public void OnArrowMissed()
    {
        arrowInFlight = false;
        CheckOutOfArrows();
    }

    private void CheckOutOfArrows()
    {
        if (arena == null) return;

        if (arrowsRemaining <= 0 && !arrowInFlight)
        {
            AddReward(deathPenalty);
            EndEpisode();
        }
    }

    private void UpdateDrawHand()
    {
        if (drawHand == null) return;

        drawHand.localPosition = Vector3.Lerp(drawRestPos, drawFullPos, drawAmount);

        if (bowString != null && stringTop != null && stringBottom != null)
        {
            bowString.positionCount = 3;
            bowString.SetPosition(0, stringTop.position);
            bowString.SetPosition(1, drawHand.position);
            bowString.SetPosition(2, stringBottom.position);
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuousActions = actionsOut.ContinuousActions;
        var discreteActions = actionsOut.DiscreteActions;
        continuousActions[0] = 0f;
        continuousActions[1] = 0f;
        continuousActions[2] = 0f;
        continuousActions[3] = 0f;
        continuousActions[4] = 0f;
        discreteActions[0] = 0;
    }
}
