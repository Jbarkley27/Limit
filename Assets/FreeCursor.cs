using UnityEngine;


public class FreeCursor : MonoBehaviour
{
    public InputManager inputManager;

    [Header("Free Cursor")]
    public GameObject freeCursorObject;
    public float reticleSpeedInput;
    public float cursorSpeedMultiplier;
    public GameObject parentReticleSystem;



    [Header("Reticle Bounds")]
    [Range(0, 1)]
    public float xScreenBoundsPercentage;

    [Range(0, 1)]
    public float yScreenBoundsPercentage;


    public static float reticleX;
    public static float reticleY;

    private void Awake()
    {
        // Aligns the Free Cursor parent to the (0,0)
        parentReticleSystem.transform.position = new Vector3(-Screen.width / 2, -Screen.height / 2, 0);
    }

    void Update()
    {
        HandleFreeReticle();
    }




    // FREE CURSOR ============================
    [Header("Free Cursor")]

    public static Vector3 clampedPos;

    public void HandleFreeReticle()
    {
        if (InputManager.LookInput.magnitude != 0)
        {
            Vector3 smoothMovement = Vector3.Lerp(freeCursorObject.transform.position, new Vector3(
                                                                        freeCursorObject.transform.position.x + InputManager.LookInput.x * cursorSpeedMultiplier,
                                                                        freeCursorObject.transform.position.y + InputManager.LookInput.y * cursorSpeedMultiplier,
                                                                        0
                                                                        ),
                                                                        reticleSpeedInput * Time.deltaTime);



            clampedPos = new Vector3(
                                    Mathf.Clamp(smoothMovement.x, 0, Screen.width),
                                    Mathf.Clamp(smoothMovement.y, 0, Screen.height),
                                    0);

            reticleX = freeCursorObject.transform.position.x;
            reticleY = freeCursorObject.transform.position.y;

            freeCursorObject.transform.position = clampedPos;

        }

    }
}
