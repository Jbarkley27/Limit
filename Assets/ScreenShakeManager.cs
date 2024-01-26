
using System.Collections;
using UnityEngine;
using Cinemachine;


public class ScreenShakeManager : MonoBehaviour
{
    public static ScreenShakeManager Instance;
    [SerializeField] private CinemachineVirtualCamera virtualCamera;

    [Header("Shake Settings")]
    public float amplitudeGain;
    public float frequemcyGain;
    public float shakeDuration;

    public struct ShakeProfile
    {
        public float amplitudeGain;
        public float frequencyGain;
        public float shakeDuration;

        public ShakeProfile(float amp, float fre, float dur)
        {
            this.amplitudeGain = amp;
            this.frequencyGain = fre;
            this.shakeDuration = dur;
        }
    }

    public static ShakeProfile JumpProfile = new(3.5f, 5, .15f);
    public static ShakeProfile ShootProfile = new(2f, 5, .1f);
    public static ShakeProfile DashProfile = new(4.5f, 5, .4f);
    public static ShakeProfile DamagedProfile = new(2.5f, 5, .3f);


    private void Awake()
    {
        if (Instance != null)
        {
            Debug.LogError("Found a Screen Shake Manager object, destroying new one.");
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        ResetScreenShake();
    }

    public void DoShake(ShakeProfile profile)
    {
        StartCoroutine(Shake(profile));
    }

    public IEnumerator Shake(ShakeProfile profile)
    {
        CinemachineBasicMultiChannelPerlin cinPerlinChannel = virtualCamera.GetCinemachineComponent<CinemachineBasicMultiChannelPerlin>();
        cinPerlinChannel.m_AmplitudeGain = profile.amplitudeGain;
        cinPerlinChannel.m_FrequencyGain = profile.frequencyGain;


        yield return new WaitForSeconds(profile.shakeDuration / 2);


        ResetScreenShake();
    }

    public void ResetScreenShake()
    {
        CinemachineBasicMultiChannelPerlin cinPerlinChannel = virtualCamera.GetCinemachineComponent<CinemachineBasicMultiChannelPerlin>();
        cinPerlinChannel.m_AmplitudeGain = 0;
        cinPerlinChannel.m_FrequencyGain = 0;

    }
}
