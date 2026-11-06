
using System.Collections;
using UnityEngine;

public class MoleHole : MonoBehaviour
{
    [Header("Mole Object")]
    [SerializeField] private GameObject moleObject;
    [SerializeField] private Collider moleCollider;

    private MoleGameManager manager;
    private Coroutine hideRoutine;
    private bool visible;
    private bool alreadyHit;
    private bool fastMole;

    public bool IsVisible => visible;

    private void Awake()
    {
        if (moleObject == null)
        {
            Debug.LogError($"[{name}] Mole Object is not assigned!", this);
            enabled = false;
            return;
        }

        if (moleCollider == null)
            moleCollider = moleObject.GetComponent<Collider>();

        if (moleCollider == null)
            Debug.LogError($"[{name}] Mole Collider is missing!", this);

        HideImmediately();
    }

    public void Initialize(MoleGameManager gameManager)
    {
        manager = gameManager;

        Debug.Log($"[{name}] Manager connected: {manager != null}", this);
    }

    public void ShowMole(bool isFast, float visibleSeconds)
    {
        if (!enabled || moleObject == null)
            return;

        if (hideRoutine != null)
            StopCoroutine(hideRoutine);

        fastMole = isFast;
        alreadyHit = false;
        visible = true;

        moleObject.SetActive(true);

        if (moleCollider != null)
            moleCollider.enabled = true;

        Debug.Log($"[{name}] Mole appeared. Fast: {fastMole}", this);

        hideRoutine = StartCoroutine(HideAfterTime(visibleSeconds));
    }

    public bool TryHit()
    {
        Debug.Log($"[{name}] TryHit called. Visible: {visible}", this);

        if (!visible || alreadyHit)
        {
            Debug.Log($"[{name}] Hit rejected.", this);
            return false;
        }

        alreadyHit = true;
        visible = false;

        int points = fastMole ? 2 : 1;

        if (moleCollider != null)
            moleCollider.enabled = false;

        moleObject.SetActive(false);

        if (hideRoutine != null)
        {
            StopCoroutine(hideRoutine);
            hideRoutine = null;
        }

        if (manager != null)
        {
            Debug.Log($"[{name}] Sending {points} points to manager.", this);
            manager.OnMoleHit(points, fastMole);
        }
        else
        {
            Debug.LogError($"[{name}] Manager reference is NULL!", this);
        }

        return true;
    }

    private IEnumerator HideAfterTime(float seconds)
    {
        yield return new WaitForSeconds(seconds);

        if (visible && !alreadyHit)
        {
            visible = false;

            if (moleCollider != null)
                moleCollider.enabled = false;

            moleObject.SetActive(false);

            if (manager != null)
                manager.OnMoleMiss();
        }

        hideRoutine = null;
    }

    public void HideImmediately()
    {
        visible = false;
        alreadyHit = false;

        if (hideRoutine != null)
        {
            StopCoroutine(hideRoutine);
            hideRoutine = null;
        }

        if (moleCollider != null)
            moleCollider.enabled = false;

        if (moleObject != null)
            moleObject.SetActive(false);
    }
}