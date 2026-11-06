using UnityEngine;

public class TrajectorySystem : MonoBehaviour
{
    [Header("Line Renderers")]
    [SerializeField] private LineRenderer trajectoryLine;
    [SerializeField] private LineRenderer landingCircle;

    [Header("Trajectory")]
    [SerializeField, Range(2, 150)] private int trajectoryPoints = 80;
    [SerializeField, Min(0.1f)] private float maxFlightTime = 5f;
    [SerializeField, Min(0.001f)] private float previewSmoothTime = 0.025f;

    [Header("Landing Marker")]
    [SerializeField] private Collider groundCollider;
    [SerializeField] private LayerMask groundLayer = ~0;
    [SerializeField, Min(0.05f)] private float landingCircleRadius = 0.5f;
    [SerializeField, Range(16, 128)] private int landingCircleSegments = 64;
    [SerializeField, Min(0.001f)] private float landingCircleWidth = 0.04f;
    [SerializeField, Min(0f)] private float landingCircleOffset = 0.025f;

    private Rigidbody ballRb;
    private Collider ballCollider;
    private Vector3 smoothedVelocity;
    private Vector3 smoothVelocityRef;
    private readonly RaycastHit[] groundHitBuffer = new RaycastHit[8];

    public void Initialize(Rigidbody rigidbody)
    {
        ballRb = rigidbody;
        ballCollider = rigidbody != null ? rigidbody.GetComponent<Collider>() : null;

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
    }

    public void ResetPreview()
    {
        smoothedVelocity = Vector3.zero;
        smoothVelocityRef = Vector3.zero;
        Hide();
    }

    public void Preview(Vector3 targetVelocity)
    {
        if (ballRb == null || targetVelocity.sqrMagnitude < 0.001f)
        {
            Hide();
            return;
        }

        smoothedVelocity = Vector3.SmoothDamp(
            smoothedVelocity, targetVelocity, ref smoothVelocityRef,
            previewSmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);

        if (TryCalculateLanding(smoothedVelocity, out Vector3 landingPoint,
            out float landingTime, out Vector3 groundNormal))
        {
            DrawTrajectory(smoothedVelocity, landingPoint, landingTime);
            DrawLandingCircle(landingPoint, groundNormal);
        }
        else
        {
            if (trajectoryLine != null)
                trajectoryLine.positionCount = 0;

            HideLandingCircle();
        }
    }

    public void Hide()
    {
        if (trajectoryLine != null)
        {
            trajectoryLine.positionCount = 0;
            trajectoryLine.enabled = false;
        }

        HideLandingCircle();
    }

    private bool TryCalculateLanding(Vector3 velocity, out Vector3 landingPoint,
        out float landingTime, out Vector3 groundNormal)
    {
        landingPoint = ballRb.position;
        landingTime = 0f;
        groundNormal = Vector3.up;

        if (velocity.sqrMagnitude < 0.001f || Physics.gravity.y >= -0.001f)
            return false;

        Vector3 rayOrigin = ballRb.position + Vector3.up * 20f;

        if (!TryGetGroundHit(new Ray(rayOrigin, Vector3.down), 40f, out RaycastHit startHit))
            return false;

        groundNormal = startHit.normal;

        float targetY = startHit.point.y + GetApproximateBallRadius();
        float a = 0.5f * Physics.gravity.y;
        float b = velocity.y;
        float c = ballRb.position.y - targetY;
        float discriminant = b * b - 4f * a * c;

        if (discriminant < 0f)
            return false;

        float sqrtD = Mathf.Sqrt(discriminant);
        float t1 = (-b - sqrtD) / (2f * a);
        float t2 = (-b + sqrtD) / (2f * a);
        float time = Mathf.Max(t1, t2);

        if (time <= 0f)
            time = Mathf.Min(t1, t2);

        if (time <= 0f || float.IsNaN(time) || float.IsInfinity(time))
            return false;

        landingTime = Mathf.Min(time, maxFlightTime);

        Vector3 predicted = ballRb.position + velocity * landingTime
            + 0.5f * Physics.gravity * landingTime * landingTime;

        Vector3 landingRayOrigin = new Vector3(
            predicted.x,
            Mathf.Max(predicted.y, startHit.point.y) + 20f,
            predicted.z);

        if (TryGetGroundHit(new Ray(landingRayOrigin, Vector3.down), 40f, out RaycastHit hit))
        {
            landingPoint = hit.point + hit.normal * landingCircleOffset;
            groundNormal = hit.normal;
        }
        else
        {
            landingPoint = new Vector3(
                predicted.x, startHit.point.y + landingCircleOffset, predicted.z);
        }

        return true;
    }

    private bool TryGetGroundHit(Ray ray, float distance, out RaycastHit hit)
    {
        if (groundCollider != null)
            return groundCollider.Raycast(ray, out hit, distance);

        int hitCount = Physics.RaycastNonAlloc(
            ray, groundHitBuffer, distance, groundLayer, QueryTriggerInteraction.Ignore);

        float nearestDistance = float.PositiveInfinity;
        int nearestIndex = -1;

        for (int i = 0; i < hitCount; i++)
        {
            Transform hitTransform = groundHitBuffer[i].transform;

            if (ballRb != null &&
                (hitTransform == ballRb.transform || hitTransform.IsChildOf(ballRb.transform)))
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
        SphereCollider sphere = ballRb != null ? ballRb.GetComponent<SphereCollider>() : null;

        if (sphere != null)
        {
            Vector3 scale = sphere.transform.lossyScale;
            return sphere.radius * Mathf.Max(
                Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        }

        return ballCollider != null ? ballCollider.bounds.extents.y : 0.1f;
    }

    private void DrawTrajectory(Vector3 velocity, Vector3 landingPoint, float landingTime)
    {
        if (trajectoryLine == null)
            return;

        int points = Mathf.Clamp(trajectoryPoints, 2, 150);
        float duration = Mathf.Clamp(landingTime, 0.01f, maxFlightTime);

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
            float time = (i / (float)(points - 1)) * duration;
            Vector3 point = start + velocity * time + 0.5f * gravity * time * time;

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

        Vector3 axisA = Vector3.Cross(normal, Vector3.forward);

        if (axisA.sqrMagnitude < 0.001f)
            axisA = Vector3.Cross(normal, Vector3.right);

        axisA.Normalize();
        Vector3 axisB = Vector3.Cross(normal, axisA).normalized;

        for (int i = 0; i < segments; i++)
        {
            float angle = (i / (float)segments) * Mathf.PI * 2f;
            Vector3 point = center
                + axisA * Mathf.Cos(angle) * landingCircleRadius
                + axisB * Mathf.Sin(angle) * landingCircleRadius;

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
}
