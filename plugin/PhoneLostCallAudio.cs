using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SpacePhone;

public sealed partial class PhoneBehaviour
{
    private AudioSource? lostCallSignalAudio;
    private AudioClip? lostCallSignalClip;
    // Original synthesized radio cue. The UI calls this a signal, never a recorded human voice.
    [HideFromIl2Cpp] private void PlayLostCallSignal()
    {
        if (state?.LostCall.OriginalVoiceUnlocked != true) return;
        lostCallSignalAudio ??= gameObject.AddComponent<AudioSource>();
        if (lostCallSignalClip == null)
        {
            const int sampleRate = 22050, seconds = 7;
            var samples = new float[sampleRate * seconds];
            uint noise = 0x51ad3201;
            for (int i = 0; i < samples.Length; i++)
            {
                double t = i / (double)sampleRate;
                noise = noise * 1664525u + 1013904223u;
                double hiss = ((noise >> 8) / 8388607.5 - 1) * .004;
                double tone = 0;
                for (int pulse = 0; pulse < 3; pulse++)
                {
                    double local = t - (.6 + pulse * .45);
                    if (local >= 0 && local < .18)
                        tone += Math.Sin(t * 880 * Math.PI * 2) * .065 * Math.Sin(local / .18 * Math.PI);
                }
                double tail = Math.Max(0, 1 - Math.Max(0, t - 5) / 2);
                samples[i] = (float)((hiss + tone) * tail * Math.Min(1, t * 5));
            }
            lostCallSignalClip = AudioClip.Create("SpacePhone original night route signal", samples.Length, 1, sampleRate, false);
            lostCallSignalClip.SetData(samples, 0);
        }
        lostCallSignalAudio.clip = lostCallSignalClip;
        lostCallSignalAudio.loop = false;
        lostCallSignalAudio.volume = state.MusicVolume;
        lostCallSignalAudio.Play();
        message = "夜航信号 · 播报文字留在回执中";
    }
    [HideFromIl2Cpp] private void StopLostCallSignal() => lostCallSignalAudio?.Stop();
    [HideFromIl2Cpp] private void DisposeLostCallSignal()
    {
        lostCallSignalAudio?.Stop();
        if (lostCallSignalClip != null) Object.Destroy(lostCallSignalClip);
        if (lostCallSignalAudio != null) Object.Destroy(lostCallSignalAudio);
        lostCallSignalClip = null; lostCallSignalAudio = null;
    }
}
