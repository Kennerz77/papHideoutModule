using System;
using EFT;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PaPHideoutModule
{
    /// <summary>
    /// In place of BO3's knuckle crack: the "All right" gesture with its "Good work" voice line, through the same method
    /// as the gesture wheel and the quick phrase key. Gestures are arm animations, so they only show while the arms are
    /// out (weapons mode). The game's voice is quiet under the machine's sting, so the same line is layered on top, in
    /// step with it (about three times as loud at full "Voice line" volume).
    /// </summary>
    internal static class PaPGestures
    {
        private const int Gesture = 203; // EInteraction "All right"
        private const EPhraseTrigger Phrase = EPhraseTrigger.GoodWork;

        private static BaseSpeaker _listening;
        private static AudioSource _layer;

        public static void Play(HideoutPlayerOwner owner)
        {
            if (owner == null || owner.HideoutPlayer == null)
                return;
            try
            {
                ListenFor(owner.HideoutPlayer.Speaker);
                owner.PlayPhraseOrGesture(Gesture, false);
                owner.PlayPhraseOrGesture((int)Phrase, false);
            }
            catch (Exception e)
            {
                PaPHideoutModulePlugin.Log.LogWarning($"Gesture and voice line failed: {e.Message}");
            }
        }

        // The speaker says which clip it picked; that clip is layered once, then we stop listening
        private static void ListenFor(BaseSpeaker speaker)
        {
            if (_listening != null)
                _listening.OnPhraseTold -= OnPhraseTold;
            _listening = speaker;
            if (speaker != null)
                speaker.OnPhraseTold += OnPhraseTold;
        }

        private static void OnPhraseTold(EPhraseTrigger trigger, TaggedClip clip, TagBank bank, BaseSpeaker speaker)
        {
            if (trigger != Phrase)
                return;
            speaker.OnPhraseTold -= OnPhraseTold;
            _listening = null;
            if (clip?.Clip == null || PaPHideoutModulePlugin.VoiceVolume.Value <= 0f)
                return;
            if (_layer == null)
            {
                var go = new GameObject("PaP_VoiceLayer");
                Object.DontDestroyOnLoad(go);
                _layer = go.AddComponent<AudioSource>();
                _layer.playOnAwake = false;
                _layer.spatialBlend = 0f; // it's your own voice
            }
            // Two layers on top of the game's own: up to three times as loud at full volume
            float volume = PaPHideoutModulePlugin.VoiceVolume.Value * Mathf.Max(0.1f, clip.Volume);
            for (int i = 0; i < VoiceLayers; i++)
                _layer.PlayOneShot(clip.Clip, volume);
        }

        private const int VoiceLayers = 2;
    }
}
