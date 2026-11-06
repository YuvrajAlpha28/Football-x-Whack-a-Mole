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

    [Header("Mobile Performance")]
    [Tooltip("Requested frame rate on mobile. Use 60 for most devices; use 30 for low-end devices.")]
    [SerializeField, Range(30, 120)] private int targetFrameRate = 60;
    [Tooltip("Enable Rigidbody interpolation for smoother rendered ball movement.")]
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

    [Header("Trajectory")]
    [SerializeField, Range(2, 150)] private int trajectoryPoints = 80;
    [SerializeField, Min(0.005f)] private float trajectoryTimeStep = 0.02f;
    [SerializeField, Min(0.001f)] private float previewSmoothTime = 0.025f;

    [Header("Landing Marker")]
    [Tooltip("A second Line Renderer used only for the circular landing marker.")]
    [SerializeField] private LineRenderer landingCircle;
    [Tooltip("Assign the football field collider. This is more reliable than using a general raycast.")]
    [SerializeField] private Collider groundCollider;
    [Tooltip("Used only when Ground Collider is not assigned. Put the field on this layer.")]
    [SerializeField] private LayerMask groundLayer = ~0;
    [SerializeField, Min(0.05f)] private float landingCircleRadius = 0.5f;
    [SerializeField, Range(16, 128)] private int landingCircleSegments = 64;
    [SerializeField, Min(0.001f)] private float landingCircleWidth = 0.04f;
    [SerializeField, Min(0f)] private float landingCircleOffset = 0.025f;

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
    // Highest speed reached during this swipe. Do not let stationary drag
    // callbacks reduce aim power while the player is holding their finger.
    private float peakSwipeSpeed;

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
    private readonly RaycastHit[] groundHitBuffer = new RaycastHit[8];

    private void Awake()
    {
        // Request a stable mobile render rate. On mobile, vSyncCount is generally ignored.
        Application.targetFrameRate = targetFrameRate;
        QualitySettings.vSyncCount = 0;

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
        ballRb.interpolation = enableRigidbodyInterpolation
            ? RigidbodyInterpolation.Interpolate
            : RigidbodyInterpolation.None;
        ballRb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        ballRb.maxAngularVelocity = 20f;

        if (trajectoryLine != null)
        {
            trajectoryLine.useWorldSpace = true;
            trajectoryLine.positionCount = 0;
            trajectoryLine.numCornerVertices = 2;
            trajectoryLine.numCapVertices = 2;
            trajectoryLine.enabled = false;
        }

        if (landingCircle != null)
        {
            landingCircle.useWorldSpace = true;
            landingCircle.loop = true;
            landingCircle.positionCount = 0;
            landingCircle.widthMultiplier = landingCircleWidth;
            landingCircle.enabled = false;
        }

        aimPoint = ballRb.position;
    }

    private void Update()
    {
        if (!isSwiping || hasShot || isRespawning)
            return;

        // Frame-rate-independent smoothing. The same smoothed velocity drives
        // both the arc and landing marker, preventing them from drifting apart.
        smoothedLaunchVelocity = Vector3.SmoothDamp(
            smoothedLaunchVelocity,
            launchVelocity,
            ref previewVelocity,
            previewSmoothTime,
            Mathf.Infinity,
            Time.unscaledDeltaTime);

        if (TryCalculateLanding(
                smoothedLaunchVelocity,
                out Vector3 landingPoint,
                out float landingTime,
                out Vector3 groundNormal))
        {
            DrawTrajectory(smoothedLaunchVelocity, landingPoint, landingTime);
            DrawLandingCircle(landingPoint, groundNormal);
        }
        else
        {
            if (trajectoryLine != null)
                trajectoryLine.positionCount = 0;

            HideLandingCircle();
        }
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
        peakSwipeSpeed = 0f;

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
            // Exponential smoothing behaves consistently at different frame rates.
            float speedBlend = 1f - Mathf.Exp(-18f * deltaTime);
            currentSwipeSpeed = Mathf.Lerp(currentSwipeSpeed, instantSpeed, speedBlend);

            // Record the maximum speed reached during this gesture.
            // When the finger stops moving, currentSwipeSpeed naturally falls
            // toward zero, but peakSwipeSpeed remains unchanged.
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

        // Use peak speed, not the current speed. EventSystem can continue
        // sending OnDrag while the finger is stationary; current speed then
        // becomes zero and previously caused the aim/landing point to move back.
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

    /// <summary>
    /// Predicts where the ball centre first reaches the field surface.
    /// The returned landingPoint is on the field surface (not floating at ball-centre height).
    /// </summary>
    private bool TryCalculateLanding(
        Vector3 velocity,
        out Vector3 landingPoint,
        out float landingTime,
        out Vector3 groundNormal)
    {
        landingPoint = ballRb.position;
        landingTime = 0f;
        groundNormal = Vector3.up;

        if (velocity.sqrMagnitude < 0.001f || Physics.gravity.y >= -0.001f)
            return false;

        // Find the field directly below the ball. If Ground Collider is assigned,
        // this ray tests only that collider and cannot accidentally hit the football.
        Vector3 rayOrigin = ballRb.position + Vector3.up * 20f;
        Ray downRay = new Ray(rayOrigin, Vector3.down);

        if (!TryGetGroundHit(downRay, 40f, out RaycastHit startGroundHit))
            return false;

        groundNormal = startGroundHit.normal;

        // Approximate the football centre height at first ground contact.
        float ballRadius = GetApproximateBallRadius();
        float targetCenterY = startGroundHit.point.y + ballRadius;

        // Solve: startY + vy*t + 0.5*gy*t^2 = targetCenterY.
        float a = 0.5f * Physics.gravity.y;
        float b = velocity.y;
        float c = ballRb.position.y - targetCenterY;
        float discriminant = b * b - 4f * a * c;

        if (discriminant < 0f)
            return false;

        float sqrtD = Mathf.Sqrt(discriminant);
        float t1 = (-b - sqrtD) / (2f * a);
        float t2 = (-b + sqrtD) / (2f * a);

        // Choose the positive descending intersection.
        float candidate = Mathf.Max(t1, t2);
        if (candidate <= 0f)
            candidate = Mathf.Min(t1, t2);

        if (candidate <= 0f || float.IsNaN(candidate) || float.IsInfinity(candidate))
            return false;

        landingTime = Mathf.Min(candidate, maxFlightTime);

        Vector3 predictedCenter = ballRb.position
            + velocity * landingTime
            + 0.5f * Physics.gravity * landingTime * landingTime;

        // Cast down at the predicted X/Z to place the ring on the actual field surface.
        Vector3 landingRayOrigin = new Vector3(
            predictedCenter.x,
            Mathf.Max(predictedCenter.y, startGroundHit.point.y) + 20f,
            predictedCenter.z);

        if (TryGetGroundHit(new Ray(landingRayOrigin, Vector3.down), 40f,
                out RaycastHit landingHit))
        {
            landingPoint = landingHit.point + landingHit.normal * landingCircleOffset;
            groundNormal = landingHit.normal;
        }
        else
        {
            // Flat-field fallback: retain the predicted X/Z and known field height.
            landingPoint = new Vector3(
                predictedCenter.x,
                startGroundHit.point.y + landingCircleOffset,
                predictedCenter.z);
        }

        return true;
    }

    private bool TryGetGroundHit(Ray ray, float distance, out RaycastHit hit)
    {
        // Best option: assign the exact field collider in the Inspector.
        // This avoids allocations and prevents accidentally hitting the ball.
        if (groundCollider != null)
            return groundCollider.Raycast(ray, out hit, distance);

        // Allocation-free fallback for scenes where Ground Collider is not assigned.
        int hitCount = Physics.RaycastNonAlloc(
            ray,
            groundHitBuffer,
            distance,
            groundLayer,
            QueryTriggerInteraction.Ignore);

        float nearestDistance = float.PositiveInfinity;
        int nearestIndex = -1;

        for (int i = 0; i < hitCount; i++)
        {
            Transform hitTransform = groundHitBuffer[i].transform;

            // Ignore the football and its child colliders.
            if (hitTransform == transform || hitTransform.IsChildOf(transform))
                continue;

            if (groundHitBuffer[i].distance < nearestDistance)
            {
                nearestDistance = groundHitBuffer[i].distance;
                nearestIndex = i;
            }
        }

        if (nearestIndex >= 0)
        {
            hit = groundHitBuffer[nearestIndex];
            return true;
        }

        hit = default;
        return false;
    }

    private float GetApproximateBallRadius()
    {
        SphereCollider sphere = GetComponent<SphereCollider>();
        if (sphere != null)
        {
            Vector3 scale = sphere.transform.lossyScale;
            return sphere.radius * Mathf.Max(
                Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        }

        Collider col = GetComponent<Collider>();
        return col != null ? col.bounds.extents.y : 0.1f;
    }

    private void DrawTrajectory(Vector3 velocity, Vector3 landingPoint, float landingTime)
    {
        if (trajectoryLine == null)
            return;

        float duration = Mathf.Clamp(landingTime, 0.01f, maxFlightTime);
        int count = Mathf.Clamp(trajectoryPoints, 2, 150);

        // Evenly distribute points across the whole flight so the final point
        // always reaches the predicted landing X/Z instead of stopping early.
        int points = count;
        trajectoryLine.positionCount = points;
        trajectoryLine.enabled = true;
        trajectoryLine.useWorldSpace = true;
        trajectoryLine.numCornerVertices = 2;
        trajectoryLine.numCapVertices = 2;

        Vector3 start = ballRb.position;
        Vector3 gravity = Physics.gravity;
        float ballRadius = GetApproximateBallRadius();

        for (int i = 0; i < points; i++)
        {
            float normalizedTime = i / (float)(points - 1);
            float time = normalizedTime * duration;

            Vector3 point = start
                + velocity * time
                + 0.5f * gravity * time * time;

            // The last arc point is the ball centre at contact height, directly
            // above the centre of the ground landing ring.
            if (i == points - 1)
                point = new Vector3(landingPoint.x, landingPoint.y + ballRadius, landingPoint.z);

            trajectoryLine.SetPosition(i, point);
        }
    }

    private void DrawLandingCircle(Vector3 center, Vector3 normal)
    {
        if (landingCircle == null)
            return;

        int segments = Mathf.Clamp(landingCircleSegments, 16, 128);
        landingCircle.positionCount = segments;
        landingCircle.loop = true;
        landingCircle.useWorldSpace = true;
        landingCircle.widthMultiplier = landingCircleWidth;
        landingCircle.numCornerVertices = 2;
        landingCircle.numCapVertices = 2;

        // Build a circle in the ground plane, so it lies flat on the field.
        Vector3 axisA = Vector3.Cross(normal, Vector3.forward);
        if (axisA.sqrMagnitude < 0.001f)
            axisA = Vector3.Cross(normal, Vector3.right);

        axisA.Normalize();
        Vector3 axisB = Vector3.Cross(normal, axisA).normalized;

        for (int i = 0; i < segments; i++)
        {
            float angle = (i / (float)segments) * Mathf.PI * 2f;
            Vector3 point = center +
                axisA * (Mathf.Cos(angle) * landingCircleRadius) +
                axisB * (Mathf.Sin(angle) * landingCircleRadius);

            landingCircle.SetPosition(i, point);
        }

        landingCircle.enabled = true;
    }

    private void HideLandingCircle()
    {
        if (landingCircle == null)
            return;

        landingCircle.positionCount = 0;
        landingCircle.enabled = false;
    }

    private void DrawTrajectory(Vector3 velocity)
    {
        // Kept as a safe fallback for any existing internal call.
        if (TryCalculateLanding(velocity, out Vector3 point, out float time,
                out Vector3 normal))
        {
            DrawTrajectory(velocity, point, time);
            DrawLandingCircle(point, normal);
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
        Debug.Log("BALL HIT OBJECT: " + collision.collider.name);

        // Search the collider itself and its parents.
        MoleHole moleHole =
            collision.collider.GetComponentInParent<MoleHole>();

        if (moleHole != null)
        {
            Debug.Log("MOLE FOUND: " + moleHole.name);

            Debug.Log("MOLE VISIBLE: " + moleHole.IsVisible);

            bool result = moleHole.TryHit();

            Debug.Log("MOLE HIT RESULT: " + result);

            return;
        }

        Debug.Log("COLLISION WAS NOT A MOLE");

        if (!hasShot || isRespawning || !respawnOnGroundCollision)
            return;

        if (!string.IsNullOrEmpty(groundTag) &&
            collision.collider.CompareTag(groundTag))
        {
            BeginRespawn();
        }
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
        peakSwipeSpeed = 0f;
        calculatedFlightTime = 0f;
        aimPoint = spawnPosition;
        HideTrajectory();
    }

    private void HideTrajectory()
    {
        if (trajectoryLine != null)
        {
            trajectoryLine.positionCount = 0;
            trajectoryLine.enabled = false;
        }

        HideLandingCircle();
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
