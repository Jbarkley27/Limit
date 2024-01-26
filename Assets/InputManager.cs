using UnityEngine;
using UnityEngine.InputSystem;

public class InputManager : MonoBehaviour
{
    public static InputManager instance { get; private set; }
    public PlayerInput input;


    private void Awake()
    {
        if (instance != null)
        {
            Debug.LogError("Found an Input Manager object, destroying new one.");
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Update()
    {
        ShootPrimary();
        ShootSecondary();
    }



    #region KestralController

    [Header("Movement")]
    public static Vector2 MovementInput;

    public void Movement(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            MovementInput = context.ReadValue<Vector2>().normalized;
        }
    }

    [Header("Look")]
    public static Vector2 LookInput;

    public void Look(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            LookInput = context.ReadValue<Vector2>().normalized;
        }
    }

    [Header("Jump")]
    public static bool JumpInput;

    public void Jump(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            JumpInput = true;
        }

        else
        {
            JumpInput = false;
        }
    }

    [Header("Dash")]
    public static bool DashInput;

    public void Dash(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            DashInput = true;
        }

        else
        {
            DashInput = false;
        }
    }

    // SHOOT PRIMARY
    [Header("Shooting")]
    public bool ShootingPrimary;
    public bool BlockPrimaryShootingUntilShootButtonIsStopped;

    public bool ShootingSecondary;
    public bool BlockSecondaryShootingUntilShootButtonIsStopped;

    public void ShootPrimary()
    {
        if (input.actions["ShootingPrimary"].IsPressed())
        {
            ShootingPrimary = true;
            ShootingSecondary = false;
        }

        else if (input.actions["ShootingPrimary"].WasReleasedThisFrame())
        {
            ShootingPrimary = false;
            BlockPrimaryShootingUntilShootButtonIsStopped = false;
        }
    }




    // SHOOT PRIMARY
    public void ShootSecondary()
    {
        if (input.actions["ShootingSecondary"].IsPressed())
        {
            ShootingSecondary = true;
            ShootingPrimary = false;
        }

        else if (input.actions["ShootingSecondary"].WasReleasedThisFrame())
        {
            ShootingSecondary = false;
            BlockSecondaryShootingUntilShootButtonIsStopped = false;
        }
    }

    // TEST - used to have a button to use to test things

    public void TestButton(InputAction.CallbackContext context)
    {
        if (!context.performed) return;
        // Test Damage
        HealthManager.instance.TakeDamage(Random.Range(1, 10));
    }



    #endregion

    // UTILITIES =====================

    public void SwitchToUIMap()
    {
        Debug.Log("Switching to UI map");
        input.SwitchCurrentActionMap("UI");
    }

    public void SwitchToGameplayMap()
    {
        Debug.Log("Switching to Gameplay Map");
        input.SwitchCurrentActionMap("Gameplay");
    }

}
