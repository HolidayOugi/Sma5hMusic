using ReactiveUI.Fody.Helpers;
using Sma5hMusic.GUI.Interfaces;
using System;
using VGMMusic;

namespace Sma5hMusic.GUI.ViewModels
{
    public partial class ToneIdCreationModalWindowModel
    {
        private readonly IAudioImportService _audioImportService;
        private readonly IMessageDialog _messageDialog;
        private readonly IVGMMusicPlayer _musicPlayer;
        private bool _isUpdatingLoopFields;

        [Reactive]
        public bool IsAudioImport { get; set; }

        [Reactive]
        public bool CanApplyNormalization { get; set; }

        [Reactive]
        public bool ApplyNormalization { get; set; }

        [Reactive]
        public uint SampleRate { get; set; }

        [Reactive]
        public uint TotalSamples { get; set; }

        [Reactive]
        public uint TotalTimeMs { get; set; }

        [Reactive]
        public uint LoopStartSample { get; set; }

        [Reactive]
        public uint LoopEndSample { get; set; }

        [Reactive]
        public uint LoopStartMs { get; set; }

        [Reactive]
        public uint LoopEndMs { get; set; }

        [Reactive]
        public uint LoopStartMinutes { get; set; }

        [Reactive]
        public uint LoopStartSeconds { get; set; }

        [Reactive]
        public uint LoopStartMilliseconds { get; set; }

        [Reactive]
        public uint LoopEndMinutes { get; set; }

        [Reactive]
        public uint LoopEndSeconds { get; set; }

        [Reactive]
        public uint LoopEndMilliseconds { get; set; }

        [Reactive]
        public double WindowHeight { get; set; }

        [Reactive]
        public double WindowWidth { get; set; }

        [Reactive]
        public double WindowMinWidth { get; set; }

        public void LoadAudioImportInfo(uint sampleRate, uint totalSamples)
        {
            IsAudioImport = true;
            ApplyNormalization = false;
            CanApplyNormalization = true;

            WindowHeight = 920;
            WindowWidth = 980;
            WindowMinWidth = 900;
            SampleRate = sampleRate;
            TotalSamples = totalSamples;
            TotalTimeMs = SamplesToMs(totalSamples);
            LoopStartSample = 0;
            LoopEndSample = totalSamples;
            ClearAutoLoopPoints();
        }

        public void LoadNus3AudioImportInfo()
        {
            IsAudioImport = false;
            ApplyNormalization = false;
            CanApplyNormalization = true;

            WindowHeight = 400;
            WindowWidth = 520;
            WindowMinWidth = 500;
            SampleRate = 0;
            TotalSamples = 0;
            TotalTimeMs = 0;
            LoopStartSample = 0;
            LoopEndSample = 0;
            LoopStartMs = 0;
            LoopEndMs = 0;
            SetLoopStartTimeParts(0);
            SetLoopEndTimeParts(0);
            ClearAutoLoopPoints();
        }

        public void ClearAudioImportInfo()
        {
            IsAudioImport = false;
            ApplyNormalization = false;
            CanApplyNormalization = false;

            WindowHeight = 400;
            WindowWidth = 520;
            WindowMinWidth = 500;
            SampleRate = 0;
            TotalSamples = 0;
            TotalTimeMs = 0;
            LoopStartSample = 0;
            LoopEndSample = 0;
            LoopStartMs = 0;
            LoopEndMs = 0;
            SetLoopStartTimeParts(0);
            SetLoopEndTimeParts(0);
            ClearAutoLoopPoints();
        }

        private void ResetLoopDefaults()
        {
            if (!IsAudioImport)
                return;

            LoopStartSample = 0;
            LoopEndSample = TotalSamples;
            SelectedAutoLoop = null;
        }

        private void UpdateLoopStartMsFromSample(uint sample)
        {
            if (_isUpdatingLoopFields)
                return;

            UpdateLoopFields(() =>
            {
                var milliseconds = SamplesToMs(sample);
                LoopStartMs = milliseconds;
                SetLoopStartTimeParts(milliseconds);
            });
        }

        private void UpdateLoopEndMsFromSample(uint sample)
        {
            if (_isUpdatingLoopFields)
                return;

            UpdateLoopFields(() =>
            {
                var milliseconds = SamplesToMs(sample);
                LoopEndMs = milliseconds;
                SetLoopEndTimeParts(milliseconds);
            });
        }

        private void UpdateLoopStartSampleFromMs(uint milliseconds)
        {
            if (_isUpdatingLoopFields)
                return;

            UpdateLoopFields(() =>
            {
                LoopStartSample = MsToSamples(milliseconds);
                SetLoopStartTimeParts(milliseconds);
            });
        }

        private void UpdateLoopEndSampleFromMs(uint milliseconds)
        {
            if (_isUpdatingLoopFields)
                return;

            UpdateLoopFields(() =>
            {
                LoopEndSample = MsToSamples(milliseconds);
                SetLoopEndTimeParts(milliseconds);
            });
        }

        private void UpdateLoopStartFromTimeParts()
        {
            if (_isUpdatingLoopFields)
                return;

            UpdateLoopFields(() =>
            {
                var milliseconds = ComposeMilliseconds(LoopStartMinutes, LoopStartSeconds, LoopStartMilliseconds);
                LoopStartMs = milliseconds;
                LoopStartSample = MsToSamples(milliseconds);
            });
        }

        private void UpdateLoopEndFromTimeParts()
        {
            if (_isUpdatingLoopFields)
                return;

            UpdateLoopFields(() =>
            {
                var milliseconds = ComposeMilliseconds(LoopEndMinutes, LoopEndSeconds, LoopEndMilliseconds);
                LoopEndMs = milliseconds;
                LoopEndSample = MsToSamples(milliseconds);
            });
        }

        private void SetLoopStartTimeParts(uint milliseconds)
        {
            SplitMilliseconds(milliseconds, out var minutes, out var seconds, out var remainingMilliseconds);
            LoopStartMinutes = minutes;
            LoopStartSeconds = seconds;
            LoopStartMilliseconds = remainingMilliseconds;
        }

        private void SetLoopEndTimeParts(uint milliseconds)
        {
            SplitMilliseconds(milliseconds, out var minutes, out var seconds, out var remainingMilliseconds);
            LoopEndMinutes = minutes;
            LoopEndSeconds = seconds;
            LoopEndMilliseconds = remainingMilliseconds;
        }

        private void UpdateLoopFields(Action update)
        {
            _isUpdatingLoopFields = true;
            update();
            _isUpdatingLoopFields = false;
        }

        private uint SamplesToMs(uint sample)
        {
            return SampleRate == 0 ? 0 : (uint)Math.Round(sample * 1000.0 / SampleRate);
        }

        private uint MsToSamples(uint milliseconds)
        {
            if (SampleRate == 0)
                return 0;

            var samples = Math.Round(milliseconds * (double)SampleRate / 1000.0);
            if (samples <= 0)
                return 0;

            var maxSamples = TotalSamples > 0 ? TotalSamples : uint.MaxValue;
            return samples >= maxSamples ? maxSamples : (uint)samples;
        }

        private static uint ComposeMilliseconds(uint minutes, uint seconds, uint milliseconds)
        {
            var total = minutes * 60000.0 + seconds * 1000.0 + milliseconds;
            return total >= uint.MaxValue ? uint.MaxValue : (uint)Math.Round(total);
        }

        private static void SplitMilliseconds(uint milliseconds, out uint minutes, out uint seconds, out uint remainingMilliseconds)
        {
            minutes = milliseconds / 60000;
            var remainder = milliseconds % 60000;
            seconds = remainder / 1000;
            remainingMilliseconds = remainder % 1000;
        }
    }
}
