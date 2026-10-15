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

    private float musicBase = -1f, sfxBase = -1f;

    /// <summary>Settings menu volumes (0..1), relative to the volumes set on the AudioSources in the scene.</summary>
    public void SetVolumes(float music, float sfx)
    {
        if (musicAS != null) { if (musicBase < 0f) musicBase = musicAS.volume; musicAS.volume = musicBase * Mathf.Clamp01(music); }
        if (sfxAS != null) { if (sfxBase < 0f) sfxBase = sfxAS.volume; sfxAS.volume = sfxBase * Mathf.Clamp01(sfx); }
        if (waterAS != null) { if (waterBase < 0f) waterBase = waterAS.volume; waterAS.volume = waterBase * Mathf.Clamp01(sfx); }
    }
    private float waterBase = -1f;

    public void PlayHarvest()
    {
        AudioClip clip = harvestSFX != null ? harvestSFX : interactSFX;
        if (sfxAS != null && clip != null) sfxAS.PlayOneShot(clip);
    }
}
