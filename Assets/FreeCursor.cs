using UnityEngine;
using UnityEngine.UI;

public class FreeCursor : MonoBehaviour
{
    public InputManager inputManager;

    [Header("Reticle")]
    public GameObject reticleGameObject;
    public float reticleSpeedInput;
    public float cursorSpeedMultiplier;
    public GameObject parentReticleSystem;



    [Header("Reticle Bounds")]
    [Range(0, 1)]
    public float xScreenBoundsPercentage;

    [Range(0, 1)]
    public float yScreenBoundsPercentage;
    public float bottomScreenOffset;


    public static float reticleX;
    public static float reticleY;


    [Header("Aim Assistance")]
    public GameObject testMarker;
    public Transform enemyTestMarker;

    private void Awake()
    {
        parentReticleSystem.transform.position = new Vector3(-Screen.width / 2, -Screen.height / 2, 0);
    }

    void Update()
    {
        HandleFreeReticle();
        //Debug.Log("Mouse -- " + Input.mousePosition);
        testMarker.transform.position = Camera.main.WorldToScreenPoint(enemyTestMarker.position);
    }




    // FREE CURSOR ============================
    [Header("Free Cursor")]
    public static Vector3 clampedPos;
    public void HandleFreeReticle()
    {
        if (InputManager.LookInput.magnitude != 0)
        {
            Vector3 smoothMovement = Vector3.Lerp(reticleGameObject.transform.position, new Vector3(
                                                                        reticleGameObject.transform.position.x + InputManager.LookInput.x * cursorSpeedMultiplier,
                                                                        reticleGameObject.transform.position.y + InputManager.LookInput.y * cursorSpeedMultiplier,
                                                                        0
                                                                        ),
                                                                        reticleSpeedInput * Time.deltaTime);



            clampedPos = new Vector3(
                                            Mathf.Clamp(smoothMovement.x, 0, Screen.width),
                                            Mathf.Clamp(smoothMovement.y, 0, Screen.height),
                                            0);

            reticleX = reticleGameObject.transform.position.x;
            reticleY = reticleGameObject.transform.position.y;

            reticleGameObject.transform.position = clampedPos;

        }

    }
}
