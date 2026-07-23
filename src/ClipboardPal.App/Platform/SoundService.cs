using System.Diagnostics;
using System.Media;
using System.Runtime.Versioning;
using ClipboardPal.Core.Abstractions;
using ClipboardPal.Core.Models;
using ClipboardPal.Core.Services;

namespace ClipboardPal.Platform;

public sealed class SoundService : ISoundService
{
    private readonly string _soundsDir;
    private readonly object _gate = new();
    private bool _wavReady;

    public SoundService(HistoryStore store)
    {
        _soundsDir = Path.Combine(store.RootDirectory, "sounds");
        Directory.CreateDirectory(_soundsDir);
    }

    public void PlayCopySound(CopySoundKind kind)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                WindowsSound.Play(kind);
                return;
            }

            EnsureWavFiles();
            var path = Path.Combine(_soundsDir, kind switch
            {
                CopySoundKind.Soft => "soft.wav",
                CopySoundKind.Click => "click.wav",
                CopySoundKind.Pop => "pop.wav",
                _ => "soft.wav"
            });

            if (!File.Exists(path))
            {
                Console.Beep();
                return;
            }

            PlayUnix(path);
        }
        catch
        {
            // ignore audio failures
        }
    }

    private void EnsureWavFiles()
    {
        lock (_gate)
        {
            if (_wavReady)
                return;

            WriteToneWav(Path.Combine(_soundsDir, "soft.wav"), frequencyHz: 880, ms: 60, volume: 0.25);
            WriteToneWav(Path.Combine(_soundsDir, "click.wav"), frequencyHz: 1400, ms: 35, volume: 0.35);
            WriteToneWav(Path.Combine(_soundsDir, "pop.wav"), frequencyHz: 660, ms: 90, volume: 0.3);
            _wavReady = true;
        }
    }

    private static void PlayUnix(string wavPath)
    {
        if (OperatingSystem.IsMacOS())
        {
            RunDetached("afplay", wavPath);
            return;
        }

        if (RunDetached("paplay", wavPath))
            return;
        if (RunDetached("aplay", wavPath))
            return;
        if (RunDetached("ffplay", "-nodisp", "-autoexit", "-loglevel", "quiet", wavPath))
            return;

        Console.Beep();
    }

    private static bool RunDetached(string fileName, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            foreach (var a in args)
                psi.ArgumentList.Add(a);

            using var proc = Process.Start(psi);
            return proc is not null;
        }
        catch
        {
            return false;
        }
    }

    private static void WriteToneWav(string path, double frequencyHz, int ms, double volume)
    {
        if (File.Exists(path))
            return;

        const int sampleRate = 22050;
        var sampleCount = sampleRate * ms / 1000;
        var dataSize = sampleCount * 2;
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);

        // RIFF header
        bw.Write("RIFF"u8);
        bw.Write(36 + dataSize);
        bw.Write("WAVE"u8);
        bw.Write("fmt "u8);
        bw.Write(16);
        bw.Write((short)1); // PCM
        bw.Write((short)1); // mono
        bw.Write(sampleRate);
        bw.Write(sampleRate * 2);
        bw.Write((short)2);
        bw.Write((short)16);
        bw.Write("data"u8);
        bw.Write(dataSize);

        for (var i = 0; i < sampleCount; i++)
        {
            var t = (double)i / sampleRate;
            var envelope = Math.Min(1.0, Math.Min(i / 200.0, (sampleCount - i) / 400.0));
            var sample = Math.Sin(2 * Math.PI * frequencyHz * t) * volume * envelope;
            bw.Write((short)(sample * short.MaxValue));
        }
    }
}

[SupportedOSPlatform("windows")]
file static class WindowsSound
{
    public static void Play(CopySoundKind kind)
    {
        switch (kind)
        {
            case CopySoundKind.Soft:
                SystemSounds.Asterisk.Play();
                break;
            case CopySoundKind.Click:
                SystemSounds.Hand.Play();
                break;
            case CopySoundKind.Pop:
                SystemSounds.Exclamation.Play();
                break;
            default:
                SystemSounds.Beep.Play();
                break;
        }
    }
}
