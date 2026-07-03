using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public float runningSpeed;
    float inputDelta = 0;
    float newX = 0;
    public float xSpeed;
    public float limitX;
    public float emgSensitivity = 1f;
    public bool emgInstantResponse = true;
    public float emgInputSmoothing = 40f;
    public EMGInputBridge inputBridge;

    [Header("EMG Lane Steps (A-D style)")]
    public bool emgUseLaneSteps = true;
    public float laneStepDistance = 0.75f;

    private Animator animator;
    private bool isRunning = false;
    private float smoothedHorizontal = 0f;

    private void Start()
    {
        animator = GetComponent<Animator>();
        SetRunning(false);
    }

    [System.Obsolete]
    void Update()
    {
        HandleMovementInput();
    }

    [System.Obsolete]
    private void HandleMovementInput()
    {
        if (!isRunning)
        {
            return;
        }

        if (inputBridge == null)
        {
            inputBridge = EMGInputBridge.Instance;
        }

        float posX = transform.position.x;
        float posZ = transform.position.z + runningSpeed * Time.deltaTime;

        if (inputBridge != null)
        {
            if (emgUseLaneSteps)
            {
                int step = inputBridge.ConsumeLaneStep();
                if (step != 0)
                {
                    posX = Mathf.Clamp(posX + step * laneStepDistance, -limitX, limitX);
                }

                inputDelta = 0f;
                smoothedHorizontal = 0f;
            }
            else
            {
                float targetHorizontal = inputBridge.GetHorizontalInput() * emgSensitivity;
                if (emgInstantResponse)
                {
                    smoothedHorizontal = targetHorizontal;
                }
                else
                {
                    smoothedHorizontal = Mathf.MoveTowards(
                        smoothedHorizontal, targetHorizontal, emgInputSmoothing * Time.deltaTime);
                }

                inputDelta = smoothedHorizontal;
                posX = Mathf.Clamp(posX + xSpeed * inputDelta * Time.deltaTime, -limitX, limitX);
            }
        }
        else
        {
            inputDelta = 0;
            smoothedHorizontal = 0f;
        }

        transform.position = new Vector3(posX, transform.position.y, posZ);
    }

    public void StartRunning()
    {
        isRunning = true;
        SetRunning(true);
    }

    public void StopRunning()
    {
        isRunning = false;
        smoothedHorizontal = 0f;
        inputDelta = 0f;
        SetRunning(false);
    }

    void SetRunning(bool running)
    {
        if (animator != null)
        {
            animator.SetBool("Running", running);
        }
    }
}
