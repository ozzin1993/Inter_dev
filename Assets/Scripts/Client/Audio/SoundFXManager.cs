using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace StrategyCore
{
    public class SoundFXManager : MonoBehaviour
    {
        public static SoundFXManager Instance { get; private set; }

        [Tooltip("Each subsequent sound played with have lower volume, to prevent simultaneous play of multiple clips.")]
        public int maxSoundCount = 4;
        private int currentSoundCount = 0; // How many sounds currently playing

        public AudioSource soundFX;
        public AudioSource soundFXLoop;

        [Header("Mixer Groups")]
        [Tooltip("The mixer group for music")]
        public AudioMixerGroup musicGroup;
        [Tooltip("The mixer group for FX")]
        public AudioMixerGroup fxGroup;
        [Tooltip("The mixer group for Voices")]
        public AudioMixerGroup voiceGroup;

        // [Interflow fix 2026-06-26 путь1] Готовность презентации: true только на пирах с графикой (клиент/хост);
        // на headless-сервере остаётся false — Play*-методы становятся no-op по своему состоянию, а не по флагу сервера.
        private bool presentationReady;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
        }

        private void Start()
        {
            GameManager.Instance.Tick += SoundCountReset;
            // [Interflow fix 2026-06-26 путь1] Подписка Tick сохраняется на всех пирах; презентацию помечаем готовой только вне headless-сервера.
            presentationReady = !ServerBootstrap.IsHeadlessServer;
        }

        private void OnDestroy()
        {
            GameManager.Instance.Tick -= SoundCountReset;
        }

        /// <summary>
        /// Plays a command audio clip for the unit.
        /// </summary>
        /// <param name="audioClips">One clip will be chosen from the provided clips.</param>
        public void PlayCommandSound(Unit unit, AudioClip[] audioClips, float volume, bool onlyVisible = false)
        {
            if (!presentationReady) return;   // [Interflow fix 2026-06-26 путь1]
            if (unit.commandSoundTime != 0) return;

            if (onlyVisible)
            {
                // Do noy play audio if not in the camera view
                if (!Utils.IsInView(unit.transform.position)) return;
                // Do not play if FoW not visible
                if (!FogOfWar.Instance.IsVisible(unit.transform.position, SlotManager.Instance.currentTeam)) return;
            }

            // Random clip index
            int index = Random.Range(0, audioClips.Length);

            // Spawn new audioSource gameObject
            AudioSource audioSource = Instantiate(soundFX, Vector3.zero, Quaternion.identity);

            // Assign audioSource parameters
            audioSource.outputAudioMixerGroup = voiceGroup;
            audioSource.clip = audioClips[index];
            audioSource.volume = volume;
            audioSource.Play();

            // Destroy object after clip length
            unit.commandSound = audioSource.transform;
            unit.commandSoundTime = audioSource.clip.length;
            GameManager.Instance.Tick += unit.CommandSoundTimerUpdate;
        }

        // Play Single sound clip
        public void PlaySoundClip(AudioClip audioClip, float volume, AudioMixerGroup mixerGroup)
        {
            if (!presentationReady) return;   // [Interflow fix 2026-06-26 путь1]
            // Check sound count
            if (currentSoundCount == maxSoundCount) return;

            // Spawn new audioSource gameObject
            AudioSource audioSource = Instantiate(soundFX, Vector3.zero, Quaternion.identity);
            currentSoundCount++;

            // Assign audioSource parameters
            audioSource.outputAudioMixerGroup = mixerGroup;
            audioSource.clip = audioClip;
            audioSource.volume = volume / currentSoundCount;
            audioSource.Play();

            // Destroy object after clip length
            float clipLength = audioSource.clip.length;
            Destroy(audioSource.gameObject, clipLength);
        }

        // Play single sound clip at Transform (Position)
        public void PlaySoundClip(AudioClip audioClip, Transform spawnTransform, float volume, AudioMixerGroup mixerGroup)
        {
            if (!presentationReady) return;   // [Interflow fix 2026-06-26 путь1]
            // Check sound count
            if (currentSoundCount == maxSoundCount) return;

            // Do noy play audio if not in the camera view
            if (!Utils.IsInView(spawnTransform.position)) return;
            // Do not play if FoW not visible
            if (!FogOfWar.Instance.IsVisible(spawnTransform.position, SlotManager.Instance.currentTeam)) return;

            // Spawn new audioSource gameObject
            AudioSource audioSource = Instantiate(soundFX, spawnTransform.position, Quaternion.identity);
            currentSoundCount++;

            // Assign audioSource parameters
            audioSource.outputAudioMixerGroup = mixerGroup;
            audioSource.clip = audioClip;
            audioSource.volume = volume / currentSoundCount;
            audioSource.Play();

            // Destroy object after clip length
            float clipLength = audioSource.clip.length;
            Destroy(audioSource.gameObject, clipLength);
        }

        // Play random sound clip
        public void PlaySoundClip(AudioClip[] audioClip, float volume, AudioMixerGroup mixerGroup)
        {
            if (!presentationReady) return;   // [Interflow fix 2026-06-26 путь1]
            // Check sound count
            if (currentSoundCount == maxSoundCount) return;

            // Random clip index
            int index = Random.Range(0, audioClip.Length);

            // Spawn new audioSource gameObject
            AudioSource audioSource = Instantiate(soundFX, Vector3.zero, Quaternion.identity);
            currentSoundCount++;

            // Assign audioSource parameters
            audioSource.outputAudioMixerGroup = mixerGroup;
            audioSource.clip = audioClip[index];
            audioSource.volume = volume / currentSoundCount;
            audioSource.Play();

            // Destroy object after clip length
            float clipLength = audioSource.clip.length;
            Destroy(audioSource.gameObject, clipLength);
        }

        // Play random sound clip at Transform (Position)
        public void PlaySoundClip(AudioClip[] audioClip, Transform spawnTransform, float volume, AudioMixerGroup mixerGroup)
        {
            if (!presentationReady) return;   // [Interflow fix 2026-06-26 путь1]
            // Check sound count
            if (currentSoundCount == maxSoundCount) return;

            // Do noy play audio if not in the camera view
            if (!Utils.IsInView(spawnTransform.position)) return;
            // Do not play if FoW not visible
            if (!FogOfWar.Instance.IsVisible(spawnTransform.position, SlotManager.Instance.currentTeam)) return;

            // Random clip index
            int index = Random.Range(0, audioClip.Length);

            // Spawn new audioSource gameObject
            AudioSource audioSource = Instantiate(soundFX, spawnTransform.position, Quaternion.identity);
            currentSoundCount++;

            // Assign audioSource parameters
            audioSource.outputAudioMixerGroup = mixerGroup;
            audioSource.clip = audioClip[index];
            audioSource.volume = volume / currentSoundCount;
            audioSource.Play();

            // Destroy object after clip length
            float clipLength = audioSource.clip.length;
            Destroy(audioSource.gameObject, clipLength);
        }

        // Play loop sound clip. Destruction of the audioSource gameObject should be done manually.
        public Transform PlayLoopSoundClip(AudioClip audioClip, float volume, AudioMixerGroup mixerGroup)
        {
            if (!presentationReady) return null;   // [Interflow fix 2026-06-26 путь1]
            // Spawn new audioSource gameObject
            AudioSource audioSource = Instantiate(soundFXLoop, Vector3.zero, Quaternion.identity);

            // Assign audioSource parameters
            audioSource.outputAudioMixerGroup = mixerGroup;
            audioSource.clip = audioClip;
            audioSource.volume = volume;

            // Turn off position check
            audioSource.GetComponent<SoundPauser>().checkPosition = false;
            audioSource.Play();

            return audioSource.transform;
        }

        // Play loop sound clip at Transform Position. Destruction of the audioSource gameObject should be done manually.
        public Transform PlayLoopSoundClip(AudioClip audioClip, Transform spawnTransform, float volume, AudioMixerGroup mixerGroup)
        {
            if (!presentationReady) return null;   // [Interflow fix 2026-06-26 путь1]
            // Spawn new audioSource gameObject
            AudioSource audioSource = Instantiate(soundFXLoop, spawnTransform.position, Quaternion.identity);

            // Assign audioSource parameters
            audioSource.outputAudioMixerGroup = mixerGroup;
            audioSource.clip = audioClip;
            audioSource.volume = volume;

            // Position check
            bool isInView = Utils.IsInView(spawnTransform.position);
            bool isFoWVisible = FogOfWar.Instance.IsVisible(spawnTransform.position, SlotManager.Instance.currentTeam);

            if (isInView && isFoWVisible) audioSource.Play();
            else audioSource.Stop();

            return audioSource.transform;
        }

        // Play random loop sound clip. Destruction of the audioSource gameObject should be done manually.
        public Transform PlayLoopSoundClip(AudioClip[] audioClip, float volume, AudioMixerGroup mixerGroup)
        {
            if (!presentationReady) return null;   // [Interflow fix 2026-06-26 путь1]
            // Random clip index
            int index = Random.Range(0, audioClip.Length);

            // Spawn new audioSource gameObject
            AudioSource audioSource = Instantiate(soundFXLoop, Vector3.zero, Quaternion.identity);

            // Assign audioSource parameters
            audioSource.outputAudioMixerGroup = mixerGroup;
            audioSource.clip = audioClip[index];
            audioSource.volume = volume;

            // Turn off position check
            audioSource.GetComponent<SoundPauser>().checkPosition = false;
            audioSource.Play();

            return audioSource.transform;
        }

        // Play random loop sound clip at Transform Position. Destruction of the audioSource gameObject should be done manually.
        public Transform PlayLoopSoundClip(AudioClip[] audioClip, Transform spawnTransform, float volume, AudioMixerGroup mixerGroup)
        {
            if (!presentationReady) return null;   // [Interflow fix 2026-06-26 путь1]
            // Random clip index
            int index = Random.Range(0, audioClip.Length);

            // Spawn new audioSource gameObject
            AudioSource audioSource = Instantiate(soundFXLoop, spawnTransform.position, Quaternion.identity);

            // Assign audioSource parameters
            audioSource.outputAudioMixerGroup = mixerGroup;
            audioSource.clip = audioClip[index];
            audioSource.volume = volume;

            // Position check
            bool isInView = Utils.IsInView(spawnTransform.position);
            bool isFoWVisible = FogOfWar.Instance.IsVisible(spawnTransform.position, SlotManager.Instance.currentTeam);

            if (isInView && isFoWVisible) audioSource.Play();
            else audioSource.Stop();

            return audioSource.transform;
        }

        // Every GameTick we reset maxSoundCount
        private void SoundCountReset()
        {
            currentSoundCount = 0;
        }
    }
}
