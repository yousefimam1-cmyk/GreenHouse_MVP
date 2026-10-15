using UnityEngine;

public class AudioManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private AudioSource musicAS;
    [SerializeField] private AudioSource waterAS;
    [SerializeField] private AudioSource sfxAS;
    [SerializeField] private AudioClip interactSFX;
    [SerializeField] private AudioClip harvestSFX;
    public static AudioManager Instance;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void StartWaterSound()
    {
        if (waterAS != null && !waterAS.isPlaying) waterAS.Play();
    }

    public void PlaySFX()
    {
        if (sfxAS != null && interactSFX != null) sfxAS.PlayOneShot(interactSFX);
    }

    public void PlayHarvest()
    {
        AudioClip clip = harvestSFX != null ? harvestSFX : interactSFX;
        if (sfxAS != null && clip != null) sfxAS.PlayOneShot(clip);
    }
}
