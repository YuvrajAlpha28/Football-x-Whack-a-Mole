using System.Collections;
using UnityEngine;
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class FootballController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private Rigidbody ballRb;
    [SerializeField] private BoxCollider aimBoundary;
    [SerializeField] private Transform ballSpawnPoint;
    [SerializeField] private TrajectorySystem trajectorySystem;

    [Header("Multi-Ball Shooting")]
    [SerializeField] private GameObject footballPrefab;
    [SerializeField, Min(0f)] private float nextBallSpawnDelay = 0.25f;

    [Header("Mobile Performance")]
    [SerializeField, Range(30, 120)] private int targetFrameRate = 60;
    [SerializeField] private bool enableRigidbodyInterpolation = true;

    [Header("Swipe")]
    [SerializeField, Min(1f)] private float minSwipePixels = 20f;
    [SerializeField, Min(2f)] private float maxSwipePixels = 600f;
    [SerializeField, Min(0.01f)] private float swipeSensitivity = 1f;
    [SerializeField, Min(1f)] private float minSwipeSpeed = 150f;
    [SerializeField, Min(1f)] private float maxSwipeSpeed = 1800f;
    [SerializeField, Range(0f, 1f)] private float swipeSpeedInfluence = 0.2f;

    [Header("Aim")]
    [SerializeField, Min(0.1f)] private float minAimDistance = 0.5f;
    [SerializeField, Min(0.1f)] private float maxAimDistance = 8f;
    [SerializeField, Min(0f)] private float boundaryPadding = 0.1f;
    [SerializeField, Min(0.1f)] private float aimPowerExponent = 1.5f;

    [Header("Launch")]
    [SerializeField, Min(0.1f)] private float upwardLift = 5f;
    [SerializeField, Min(0.01f)] private float launchSpeedMultiplier = 1f;
    [SerializeField, Min(0.1f)] private float maxFlightTime = 5f;

    [Header("Respawn")]
    [SerializeField, Min(0f)] private float respawnAfterShot = 1.5f;
    [SerializeField, Min(0f)] private float respawnDelay = 0.05f;

    [Header("Optional Ground Respawn")]
    [SerializeField] private bool respawnOnGroundCollision = false;
    [SerializeField] private string groundTag = "Ground";

    private Vector3 spawnPosition;
    private Quaternion spawnRotation;
    private Vector2 swipeStart;
    private Vector2 swipeCurrent;
    private Vector2 previousSwipePosition;
    private float previousSwipeTime;
    private float currentSwipeSpeed;
    private float peakSwipeSpeed;
    private Vector3 launchVelocity;
    private Vector3 aimPoint;
    private float shotStartTime;
    private bool isSwiping;
    private bool hasShot;
    private bool isRespawning;
    private Coroutine respawnCoroutine;
    private bool isProjectile;
    private bool waitingForNextBall;
    private Collider ballCollider;
    private Coroutine spawnNextBallCoroutine;

    private void Awake()
    {
        Application.targetFrameRate = targetFrameRate;
        QualitySettings.vSyncCount = 0;

        if (ballRb == null)
            ballRb = GetComponent<Rigidbody>();
        ballCollider = GetComponent<Collider>();
        if (mainCamera == null)
            mainCamera = Camera.main;
        if (trajectorySystem == null)
            trajectorySystem = GetComponent<TrajectorySystem>();
        if (ballRb == null || mainCamera == null)
        {
            Debug.LogError("FootballController: Main Camera and Rigidbody must exist.", this);
            enabled = false;
            return;
        }

        spawnPosition = ballSpawnPoint != null ? ballSpawnPoint.position : ballRb.position;
        spawnRotation = ballSpawnPoint != null ? ballSpawnPoint.rotation : ballRb.rotation;
        ballRb.isKinematic = true;
        ballRb.useGravity = true;
        ballRb.interpolation = enableRigidbodyInterpolation
            ? RigidbodyInterpolation.Interpolate
            : RigidbodyInterpolation.None;
        ballRb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        ballRb.maxAngularVelocity = 20f;
        if (!isProjectile && ballCollider != null)
            ballCollider.enabled = false;

        if (trajectorySystem != null)
            trajectorySystem.Initialize(ballRb);

        aimPoint = ballRb.position;
    }

    private void Update()
    {
        if (isSwiping && !hasShot && !isRespawning && trajectorySystem != null)
            trajectorySystem.Preview(launchVelocity);
    }

    public void BeginSwipe(Vector2 screenPosition)
    {
        if (!enabled || isProjectile || hasShot || isRespawning || waitingForNextBall ||
            ballRb == null || aimBoundary == null)
            return;

        isSwiping = true;
        swipeStart = screenPosition;
        swipeCurrent = swipeStart;
        previousSwipePosition = swipeStart;
        previousSwipeTime = Time.unscaledTime;
        currentSwipeSpeed = 0f;
        peakSwipeSpeed = 0f;
        launchVelocity = Vector3.zero;
        aimPoint = ballRb.position;

        if (trajectorySystem != null)
            trajectorySystem.ResetPreview();
    }

    public void UpdateSwipe(Vector2 screenPosition)
    {
        if (isProjectile || !isSwiping || hasShot || isRespawning ||
            waitingForNextBall)
            return;

        UpdateSwipeInput(screenPosition);
        CalculateShot();
    }

    public void EndSwipe(Vector2 screenPosition)
    {
        if (isProjectile || !isSwiping || hasShot || isRespawning ||
            waitingForNextBall)
            return;

        UpdateSwipeInput(screenPosition);
        CalculateShot();
        isSwiping = false;

        float distance = Vector2.Distance(swipeStart, swipeCurrent);
        if (distance >= minSwipePixels && launchVelocity.sqrMagnitude > 0.001f)
            ShootBall();
        else if (trajectorySystem != null)
            trajectorySystem.Hide();
    }

    private void UpdateSwipeInput(Vector2 position)
    {
        swipeCurrent = position;
        float now = Time.unscaledTime;
        float deltaTime = now - previousSwipeTime;

        if (deltaTime > 0.001f)
        {
            float distance = Vector2.Distance(position, previousSwipePosition);
            float instantSpeed = distance / deltaTime;
            float speedBlend = 1f - Mathf.Exp(-18f * deltaTime);
            currentSwipeSpeed = Mathf.Lerp(currentSwipeSpeed, instantSpeed, speedBlend);
            peakSwipeSpeed = Mathf.Max(peakSwipeSpeed, currentSwipeSpeed);
        }

        previousSwipePosition = position;
        previousSwipeTime = now;
    }

    private void CalculateShot()
    {
        Vector2 swipeDelta = swipeCurrent - swipeStart;
        float swipeDistance = swipeDelta.magnitude;

        if (swipeDistance < minSwipePixels)
        {
            launchVelocity = Vector3.zero;
            return;
        }

        Vector3 screenRight = Vector3.ProjectOnPlane(mainCamera.transform.right, Vector3.up);
        Vector3 screenUp = Vector3.ProjectOnPlane(mainCamera.transform.up, Vector3.up);

        if (screenRight.sqrMagnitude < 0.0001f)
            screenRight = mainCamera.transform.forward;
        if (screenUp.sqrMagnitude < 0.0001f)
            screenUp = Vector3.Cross(Vector3.up, screenRight);

        screenRight.Normalize();
        screenUp.Normalize();

        Vector3 worldDirection = screenRight * swipeDelta.x + screenUp * swipeDelta.y;
        worldDirection = Vector3.ProjectOnPlane(worldDirection, Vector3.up);

        if (worldDirection.sqrMagnitude < 0.0001f)
        {
            launchVelocity = Vector3.zero;
            return;
        }

        worldDirection.Normalize();

        float adjustedDistance = swipeDistance * swipeSensitivity;
        float distancePower = Mathf.InverseLerp(minSwipePixels, maxSwipePixels, adjustedDistance);
        distancePower = Mathf.Pow(Mathf.Clamp01(distancePower), aimPowerExponent);

        float speedPower = Mathf.InverseLerp(minSwipeSpeed, maxSwipeSpeed, peakSwipeSpeed);
        float power = Mathf.Lerp(
            distancePower,
            Mathf.Max(distancePower, speedPower),
            swipeSpeedInfluence);

        float availableDistance = GetMaxDistanceInsideBoundary(worldDirection);
        if (availableDistance <= 0.01f)
        {
            launchVelocity = Vector3.zero;
            return;
        }

        float allowedMax = Mathf.Min(maxAimDistance, availableDistance);
        float allowedMin = Mathf.Min(minAimDistance, allowedMax);
        float aimDistance = Mathf.Lerp(allowedMin, allowedMax, power);

        Vector3 desiredPoint = ballRb.position + worldDirection * aimDistance;
        aimPoint = ClampAimToField(desiredPoint);

        Vector3 horizontalDirection = Vector3.ProjectOnPlane(
            aimPoint - ballRb.position, Vector3.up);
        float horizontalDistance = horizontalDirection.magnitude;

        if (horizontalDistance < 0.05f || Physics.gravity.y >= -0.01f ||
            upwardLift <= 0f)
        {
            launchVelocity = Vector3.zero;
            return;
        }

        horizontalDirection.Normalize();
        float verticalSpeed = upwardLift;
        float calculatedFlightTime = -2f * verticalSpeed / Physics.gravity.y;

        if (calculatedFlightTime <= 0.01f)
        {
            launchVelocity = Vector3.zero;
            return;
        }

        float horizontalSpeed = horizontalDistance / calculatedFlightTime;
        launchVelocity = horizontalDirection * horizontalSpeed * launchSpeedMultiplier
            + Vector3.up * verticalSpeed;
    }

    private float GetMaxDistanceInsideBoundary(Vector3 worldDirection)
    {
        Transform t = aimBoundary.transform;
        Vector3 localPosition = t.InverseTransformPoint(ballRb.position);
        Vector3 localDirection = t.InverseTransformVector(worldDirection);
        Vector3 min = aimBoundary.center - aimBoundary.size * 0.5f;
        Vector3 max = aimBoundary.center + aimBoundary.size * 0.5f;
        float result = float.PositiveInfinity;

        if (localDirection.x > 0.000001f)
        {
            result = Mathf.Min(result, (max.x - localPosition.x) / localDirection.x);
        }
        else if (localDirection.x < -0.000001f)
        {
            result = Mathf.Min(result, (min.x - localPosition.x) / localDirection.x);
        }

        if (localDirection.z > 0.000001f)
        {
            result = Mathf.Min(result, (max.z - localPosition.z) / localDirection.z);
        }
        else if (localDirection.z < -0.000001f)
        {
            result = Mathf.Min(result, (min.z - localPosition.z) / localDirection.z);
        }

        if (float.IsInfinity(result) || float.IsNaN(result))
            return 0f;

        return Mathf.Max(0f, result - boundaryPadding);
    }

    private Vector3 ClampAimToField(Vector3 desiredPoint)
    {
        Transform t = aimBoundary.transform;
        Vector3 local = t.InverseTransformPoint(desiredPoint);
        Vector3 min = aimBoundary.center - aimBoundary.size * 0.5f;
        Vector3 max = aimBoundary.center + aimBoundary.size * 0.5f;

        float padX = Mathf.Min(boundaryPadding, aimBoundary.size.x * 0.49f);
        float padZ = Mathf.Min(boundaryPadding, aimBoundary.size.z * 0.49f);

        local.x = Mathf.Clamp(local.x, min.x + padX, max.x - padX);
        local.z = Mathf.Clamp(local.z, min.z + padZ, max.z - padZ);

        Vector3 world = t.TransformPoint(local);
        world.y = ballRb.position.y;
        return world;
    }

    private void ShootBall()
    {
        if (hasShot || isRespawning || isProjectile || waitingForNextBall ||
            launchVelocity.sqrMagnitude < 0.001f)
            return;

        if (footballPrefab == null)
        {
            Debug.LogError("FootballController: Assign the Football Prefab in the Inspector.", this);
            return;
        }
        Vector3 shotVelocity = launchVelocity;

        if (trajectorySystem != null)
            trajectorySystem.Hide();

        isSwiping = false;
        waitingForNextBall = true;
        launchVelocity = Vector3.zero;
        GameObject spawnedBall = Instantiate(footballPrefab, spawnPosition, spawnRotation);
        FootballController projectileController = spawnedBall.GetComponent<FootballController>();

        if (projectileController == null)
        {
            Debug.LogError("Football Prefab must contain a FootballController component.", spawnedBall);
            Destroy(spawnedBall);
            waitingForNextBall = false;
            ResetShotState();
            return;
        }

        projectileController.LaunchAsProjectile(shotVelocity);
        if (spawnNextBallCoroutine != null)
            StopCoroutine(spawnNextBallCoroutine);

        spawnNextBallCoroutine = StartCoroutine(EnableNextShotAfterDelay());
    }

    private IEnumerator EnableNextShotAfterDelay()
    {
        if (nextBallSpawnDelay > 0f)
            yield return new WaitForSecondsRealtime(nextBallSpawnDelay);

        waitingForNextBall = false;
        hasShot = false;
        spawnNextBallCoroutine = null;
        ResetShotState();
    }

    private void LaunchAsProjectile(Vector3 velocity)
    {
        isProjectile = true;
        hasShot = true;
        isSwiping = false;
        shotStartTime = Time.time;

        if (trajectorySystem != null)
            trajectorySystem.Hide();

        if (ballCollider != null)
            ballCollider.enabled = true;

        ballRb.isKinematic = false;
        ballRb.useGravity = true;
        ballRb.linearVelocity = velocity;
        ballRb.angularVelocity = mainCamera.transform.right * 5f;
    }

    private void OnCollisionEnter(Collision collision)
    {
        MoleHole moleHole = collision.collider.GetComponentInParent<MoleHole>();
        if (moleHole != null)
        {
            moleHole.TryHit();
            return;
        }

        if (isProjectile)
        {
            if (!string.IsNullOrEmpty(groundTag) && collision.collider.CompareTag(groundTag))
                Destroy(gameObject);
            return;
        }

        if (!hasShot || isRespawning || !respawnOnGroundCollision)
            return;

        if (!string.IsNullOrEmpty(groundTag) && collision.collider.CompareTag(groundTag))
            BeginRespawn();
    }

    private void LateUpdate()
    {
        if (!hasShot || isRespawning || ballRb == null)
            return;
        if (Time.time - shotStartTime >= respawnAfterShot)
        {
            if (isProjectile)
                Destroy(gameObject);
            else
                BeginRespawn();
        }
    }

    private void BeginRespawn()
    {
        if (isRespawning || respawnCoroutine != null)
            return;

        respawnCoroutine = StartCoroutine(RespawnRoutine());
    }

    private IEnumerator RespawnRoutine()
    {
        isRespawning = true;
        isSwiping = false;

        if (trajectorySystem != null)
            trajectorySystem.Hide();

        ballRb.linearVelocity = Vector3.zero;
        ballRb.angularVelocity = Vector3.zero;
        ballRb.isKinematic = true;

        if (respawnDelay > 0f)
            yield return new WaitForSecondsRealtime(respawnDelay);

        ballRb.position = spawnPosition;
        ballRb.rotation = spawnRotation;
        Physics.SyncTransforms();

        ResetShotState();
        isRespawning = false;
        respawnCoroutine = null;
    }

    public void ResetBall()
    {
        if (ballRb == null)
            return;

        if (respawnCoroutine != null)
        {
            StopCoroutine(respawnCoroutine);
            respawnCoroutine = null;
        }

        isRespawning = false;
        ballRb.linearVelocity = Vector3.zero;
        ballRb.angularVelocity = Vector3.zero;
        ballRb.isKinematic = true;
        ballRb.position = spawnPosition;
        ballRb.rotation = spawnRotation;
        Physics.SyncTransforms();

        ResetShotState();
    }

    private void ResetShotState()
    {
        hasShot = false;
        isSwiping = false;
        launchVelocity = Vector3.zero;
        currentSwipeSpeed = 0f;
        peakSwipeSpeed = 0f;
        aimPoint = spawnPosition;

        if (trajectorySystem != null)
            trajectorySystem.ResetPreview();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
        {
            Application.targetFrameRate = targetFrameRate;
            QualitySettings.vSyncCount = 0;
        }
    }

    private void OnDisable()
    {
        if (spawnNextBallCoroutine != null)
        {
            StopCoroutine(spawnNextBallCoroutine);
            spawnNextBallCoroutine = null;
        }

        if (respawnCoroutine != null)
        {
            StopCoroutine(respawnCoroutine);
            respawnCoroutine = null;
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (aimBoundary != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(aimBoundary.bounds.center, aimBoundary.bounds.size);
        }

        if (Application.isPlaying)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(aimPoint, 0.12f);
        }
    }
}
