using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Swipe-to-shoot football controller.
/// Attach this to the football GameObject (which must have a Collider and Rigidbody).
/// For pointer callbacks on a 3D object, the scene also needs an EventSystem and
/// a PhysicsRaycaster on the camera. See setup notes below.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class SwipeFootballShooter : MonoBehaviour,
    IPointerDownHandler,
    IDragHandler,
    IPointerUpHandler
{
    [Header("References")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private Rigidbody ballRb;
    [SerializeField] private LineRenderer trajectoryLine;
    [SerializeField] private BoxCollider aimBoundary;
    [SerializeField] private Transform ballSpawnPoint;

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

    [Header("Trajectory")]
    [SerializeField, Range(2, 100)] private int trajectoryPoints = 40;
    [SerializeField, Min(0.005f)] private float trajectoryTimeStep = 0.04f;
    [SerializeField, Min(0.001f)] private float previewSmoothTime = 0.04f;

    [Header("Respawn")]
    [SerializeField, Min(0f)] private float respawnDelay = 1f;
    [SerializeField] private bool respawnOnGroundCollision = true;
    [SerializeField] private string groundTag = "Ground";

    private Vector3 spawnPosition;
    private Quaternion spawnRotation;

    private Vector2 swipeStart;
    private Vector2 swipeCurrent;
    private Vector2 previousSwipePosition;
    private float previousSwipeTime;
    private float currentSwipeSpeed;

    private Vector3 launchVelocity;
    private Vector3 smoothedLaunchVelocity;
    private Vector3 previewVelocity;
    private Vector3 aimPoint;

    private float shotStartTime;
    private float calculatedFlightTime;

    private bool isSwiping;
    private bool hasShot;
    private bool isRespawning;
    private Coroutine respawnCoroutine;

    private void Awake()
    {
        // Resolve references BEFORE reading Rigidbody values. This avoids the
        // null-reference error in the previous version when ballRb was unassigned.
        if (ballRb == null)
            ballRb = GetComponent<Rigidbody>();

        if (mainCamera == null)
            mainCamera = Camera.main;

        if (ballRb == null || mainCamera == null || aimBoundary == null)
        {
            Debug.LogError(
                "SwipeFootballShooter: Assign an Aim Boundary and ensure a Main Camera and Rigidbody exist.",
                this);
            enabled = false;
            return;
        }

        spawnPosition = ballSpawnPoint != null ? ballSpawnPoint.position : ballRb.position;
        spawnRotation = ballSpawnPoint != null ? ballSpawnPoint.rotation : ballRb.rotation;

        ballRb.isKinematic = true;
        ballRb.useGravity = true;
        ballRb.interpolation = RigidbodyInterpolation.Interpolate;

        if (trajectoryLine != null)
        {
            trajectoryLine.useWorldSpace = true;
            trajectoryLine.positionCount = 0;
            trajectoryLine.enabled = false;
        }

        aimPoint = ballRb.position;
    }

    private void Update()
    {
        if (!isSwiping || hasShot || isRespawning)
            return;

        smoothedLaunchVelocity = Vector3.SmoothDamp(
            smoothedLaunchVelocity,
            launchVelocity,
            ref previewVelocity,
            previewSmoothTime);

        DrawTrajectory(smoothedLaunchVelocity);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!enabled || hasShot || isRespawning || ballRb == null)
            return;

        isSwiping = true;
        swipeStart = eventData.position;
        swipeCurrent = swipeStart;
        previousSwipePosition = swipeStart;
        previousSwipeTime = Time.unscaledTime;
        currentSwipeSpeed = 0f;

        launchVelocity = Vector3.zero;
        smoothedLaunchVelocity = Vector3.zero;
        previewVelocity = Vector3.zero;
        aimPoint = ballRb.position;

        if (trajectoryLine != null)
        {
            trajectoryLine.positionCount = 0;
            trajectoryLine.enabled = true;
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!isSwiping || hasShot || isRespawning)
            return;

        UpdateSwipeInput(eventData.position);
        CalculateShot();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!isSwiping || hasShot || isRespawning)
            return;

        // Include the final pointer position; otherwise the last few pixels
        // of a fast swipe can be lost.
        UpdateSwipeInput(eventData.position);
        CalculateShot();

        isSwiping = false;

        float distance = Vector2.Distance(swipeStart, swipeCurrent);
        if (distance >= minSwipePixels && launchVelocity.sqrMagnitude > 0.001f)
            ShootBall();
        else
            HideTrajectory();
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
            currentSwipeSpeed = Mathf.Lerp(currentSwipeSpeed, instantSpeed, 0.35f);
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

        float speedPower = Mathf.InverseLerp(minSwipeSpeed, maxSwipeSpeed, currentSwipeSpeed);
        float power = Mathf.Lerp(distancePower, Mathf.Max(distancePower, speedPower), swipeSpeedInfluence);

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

        Vector3 horizontalDirection = Vector3.ProjectOnPlane(aimPoint - ballRb.position, Vector3.up);
        float horizontalDistance = horizontalDirection.magnitude;

        if (horizontalDistance < 0.05f || Physics.gravity.y >= -0.01f || upwardLift <= 0f)
        {
            launchVelocity = Vector3.zero;
            return;
        }

        horizontalDirection.Normalize();

        // Time to return to the launch height, using the actual vertical
        // launch speed. The preview uses the same velocity and gravity.
        float verticalSpeed = upwardLift;
        calculatedFlightTime = -2f * verticalSpeed / Physics.gravity.y;

        if (calculatedFlightTime <= 0.01f)
        {
            launchVelocity = Vector3.zero;
            return;
        }

        float horizontalSpeed = horizontalDistance / calculatedFlightTime;
        launchVelocity =
            horizontalDirection * horizontalSpeed * launchSpeedMultiplier +
            Vector3.up * verticalSpeed;
    }

    // Returns the distance in world metres to the first X/Z face of the
    // oriented BoxCollider. InverseTransformVector accounts for collider scale.
    private float GetMaxDistanceInsideBoundary(Vector3 worldDirection)
    {
        Transform t = aimBoundary.transform;
        Vector3 localPosition = t.InverseTransformPoint(ballRb.position);
        Vector3 localDirection = t.InverseTransformVector(worldDirection);

        Vector3 min = aimBoundary.center - aimBoundary.size * 0.5f;
        Vector3 max = aimBoundary.center + aimBoundary.size * 0.5f;
        float result = float.PositiveInfinity;

        if (localDirection.x > 0.000001f)
            result = Mathf.Min(result, (max.x - localPosition.x) / localDirection.x);
        else if (localDirection.x < -0.000001f)
            result = Mathf.Min(result, (min.x - localPosition.x) / localDirection.x);

        if (localDirection.z > 0.000001f)
            result = Mathf.Min(result, (max.z - localPosition.z) / localDirection.z);
        else if (localDirection.z < -0.000001f)
            result = Mathf.Min(result, (min.z - localPosition.z) / localDirection.z);

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

    private void DrawTrajectory(Vector3 velocity)
    {
        if (trajectoryLine == null)
            return;

        if (velocity.sqrMagnitude < 0.001f)
        {
            trajectoryLine.positionCount = 0;
            return;
        }

        float step = Mathf.Max(0.005f, trajectoryTimeStep);
        int count = Mathf.Clamp(trajectoryPoints, 2, 100);

        // Draw over the predicted flight duration, capped for safety.
        float duration = Mathf.Clamp(calculatedFlightTime, step, maxFlightTime);
        int points = Mathf.Clamp(Mathf.CeilToInt(duration / step) + 1, 2, count);

        trajectoryLine.positionCount = points;
        Vector3 start = ballRb.position;
        Vector3 gravity = Physics.gravity;

        for (int i = 0; i < points; i++)
        {
            float time = Mathf.Min(i * step, duration);
            Vector3 point = start + velocity * time + 0.5f * gravity * time * time;
            trajectoryLine.SetPosition(i, point);
        }
    }

    private void ShootBall()
    {
        if (hasShot || isRespawning || launchVelocity.sqrMagnitude < 0.001f)
            return;

        hasShot = true;
        shotStartTime = Time.time;
        HideTrajectory();

        ballRb.isKinematic = false;
        ballRb.useGravity = true;
        ballRb.linearVelocity = launchVelocity;

        // Add a small roll/spin around the camera's right axis.
        ballRb.angularVelocity = mainCamera.transform.right * 5f;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!hasShot || isRespawning || !respawnOnGroundCollision)
            return;

        if (!string.IsNullOrEmpty(groundTag) && collision.collider.CompareTag(groundTag))
            BeginRespawn();
    }

    private void LateUpdate()
    {
        if (!hasShot || isRespawning || ballRb == null)
            return;

        if (ballRb.position.y < -5f || Time.time - shotStartTime >= maxFlightTime)
            BeginRespawn();
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
        HideTrajectory();

        // Stop physics immediately so the ball cannot keep rolling or trigger
        // several collision callbacks while the respawn delay is running.
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

    /// <summary>Can also be called by a UI Reset button.</summary>
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
        smoothedLaunchVelocity = Vector3.zero;
        previewVelocity = Vector3.zero;
        currentSwipeSpeed = 0f;
        calculatedFlightTime = 0f;
        aimPoint = spawnPosition;
        HideTrajectory();
    }

    private void HideTrajectory()
    {
        if (trajectoryLine == null)
            return;

        trajectoryLine.positionCount = 0;
        trajectoryLine.enabled = false;
    }

    private void OnDisable()
    {
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
