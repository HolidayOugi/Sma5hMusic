using Microsoft.Extensions.Logging;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Sma5hMusic.GUI.Models;
using System;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace Sma5hMusic.GUI.ViewModels
{
    public partial class ToneIdCreationModalWindowModel
    {
        private string _loopPreviewFile;
        private int _loopPreviewVersion;
        private bool _isCompletingPreview;
        private IDisposable _previewProgressSubscription;
        private LoopPreviewInfo _loopPreviewInfo;

        public ReactiveCommand<Unit, Unit> ActionPreviewLoop { get; }
        public ReactiveCommand<Unit, Unit> ActionStopPreview { get; }
        public ReactiveCommand<Unit, Unit> ActionResetLoopDefaults { get; }

        [Reactive]
        public bool IsPreviewProgressVisible { get; set; }

        [Reactive]
        public double PreviewProgressMaximum { get; set; }

        [Reactive]
        public double PreviewProgressValue { get; set; }

        [Reactive]
        public string PreviewProgressText { get; set; }

        public async Task ClosePreview()
        {
            StopAutoLoopStatusAnimation();
            _loopPreviewVersion++;
            await StopPreview();
            CleanupLoopPreviewFiles();
        }

        public void CleanupLoopPreviewFiles()
        {
            _audioImportService.CleanupLoopPreviews();
        }

        private async Task PreviewLoop()
        {
            try
            {
                _logger.LogInformation("Preview loop clicked. Filename={Filename}, LoopStartSample={LoopStartSample}, LoopEndSample={LoopEndSample}, LoopStartMs={LoopStartMs}, LoopEndMs={LoopEndMs}, TotalSamples={TotalSamples}",
                    Filename, LoopStartSample, LoopEndSample, LoopStartMs, LoopEndMs, TotalSamples);

                await StopPreview();
                var previewVersion = ++_loopPreviewVersion;

                var previewInfo = await _audioImportService.CreateLoopPreview(Filename, LoopStartSample, LoopEndSample, TotalSamples);
                if (previewVersion != _loopPreviewVersion)
                {
                    _logger.LogInformation("Loop preview finished after the modal was closed or another preview started. Deleting stale file {PreviewFile}.", previewInfo.Filename);
                    DeletePreviewFile(previewInfo.Filename);
                    return;
                }

                _loopPreviewFile = previewInfo.Filename;
                _loopPreviewInfo = previewInfo;
                _logger.LogInformation("Preview loop file ready. File={PreviewFile}, PreviewLengthSamples={PreviewLengthSamples}, Exists={Exists}, Length={Length}",
                    previewInfo.Filename, previewInfo.PreviewLengthSamples, File.Exists(previewInfo.Filename), File.Exists(previewInfo.Filename) ? new FileInfo(previewInfo.Filename).Length : 0);

                var played = await _musicPlayer.Play(previewInfo.Filename);
                _logger.LogInformation("Preview loop play requested. Played={Played}", played);
                if (!played)
                    await _messageDialog.ShowError("Loop preview failed", "The preview file was created, but vgmstream could not play it.");
                else
                    StartPreviewProgress();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Loop preview failed.");
                await _messageDialog.ShowError("Loop preview failed", e.Message, e);
            }
        }

        private void DeletePreviewFile(string filename)
        {
            if (string.IsNullOrEmpty(filename))
                return;

            try
            {
                if (File.Exists(filename))
                {
                    File.Delete(filename);
                    _logger.LogInformation("Deleted loop preview file {LoopPreviewFile}.", filename);
                }
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Could not delete loop preview file {LoopPreviewFile}.", filename);
            }
        }

        private async Task StopPreview()
        {
            _logger.LogInformation("StopPreview starting. PreviewFile={PreviewFile}", _loopPreviewFile);
            _isCompletingPreview = false;
            StopPreviewProgress();

            try
            {
                await _musicPlayer.Stop();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Music player stop failed while stopping preview.");
            }

            if (!string.IsNullOrEmpty(_loopPreviewFile))
            {
                DeletePreviewFile(_loopPreviewFile);
                _loopPreviewFile = null;
            }

            _logger.LogInformation("StopPreview completed.");
        }

        private void StartPreviewProgress()
        {
            DisposePreviewProgressTimer();
            _isCompletingPreview = false;
            PreviewProgressMaximum = TotalSamples;
            IsPreviewProgressVisible = true;
            UpdatePreviewProgress();
            _previewProgressSubscription = Observable.Interval(TimeSpan.FromMilliseconds(50))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => UpdatePreviewProgress());
        }

        private void StopPreviewProgress()
        {
            DisposePreviewProgressTimer();
            _loopPreviewInfo = null;
            IsPreviewProgressVisible = false;
            PreviewProgressValue = 0;
            PreviewProgressText = string.Empty;
        }

        private void DisposePreviewProgressTimer()
        {
            _previewProgressSubscription?.Dispose();
            _previewProgressSubscription = null;
        }

        private void UpdatePreviewProgress()
        {
            if (_loopPreviewInfo == null)
                return;

            var rawPreviewSample = (uint)Math.Max(0, _musicPlayer.CurrentSample);
            if (rawPreviewSample >= _loopPreviewInfo.PreviewLengthSamples)
            {
                PreviewProgressValue = Math.Min(LoopStartSample, TotalSamples);
                PreviewProgressText = "Preview finished.";
                _ = CompletePreviewPlayback();
                return;
            }

            var sourceSample = MapPreviewSampleToSourceSample(rawPreviewSample);
            PreviewProgressValue = Math.Min(sourceSample, TotalSamples);
            PreviewProgressText = $"Preview: {FormatMs(SamplesToMs((uint)PreviewProgressValue))} / {FormatMs(TotalTimeMs)} - loop from {FormatMs(LoopEndMs)} to {FormatMs(LoopStartMs)}";
        }

        private async Task CompletePreviewPlayback()
        {
            if (_isCompletingPreview)
                return;

            _isCompletingPreview = true;
            _logger.LogInformation("Loop preview reached the end of the generated preview file. Stopping preview state.");
            await StopPreview();
        }

        private uint MapPreviewSampleToSourceSample(uint previewSample)
        {
            if (_loopPreviewInfo == null)
                return 0;

            if (previewSample >= _loopPreviewInfo.SecondSegmentPreviewStartSample)
            {
                var offset = previewSample - _loopPreviewInfo.SecondSegmentPreviewStartSample;
                return _loopPreviewInfo.SecondSegmentSourceStartSample + Convert48kSamplesToSourceSamples(offset);
            }

            return _loopPreviewInfo.FirstSegmentSourceStartSample + Convert48kSamplesToSourceSamples(previewSample);
        }

        private uint Convert48kSamplesToSourceSamples(uint samples)
        {
            return SampleRate == 0 ? 0 : (uint)Math.Round(samples * (SampleRate / 48000.0));
        }

        private static string FormatMs(uint milliseconds)
        {
            var time = TimeSpan.FromMilliseconds(milliseconds);
            return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss\.fff") : time.ToString(@"m\:ss\.fff");
        }
    }
}
