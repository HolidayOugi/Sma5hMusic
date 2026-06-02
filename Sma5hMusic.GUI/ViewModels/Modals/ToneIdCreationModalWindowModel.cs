using Avalonia.Controls;
using DynamicData;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using ReactiveUI.Validation.Extensions;
using ReactiveUI.Validation.Helpers;
using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.Models;
using Sma5hMusic.GUI.Interfaces;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using VGMMusic;

namespace Sma5hMusic.GUI.ViewModels
{
    public class ToneIdCreationModalWindowModel : ReactiveValidationObject
    {
        private readonly ILogger _logger;
        private readonly IAudioImportService _audioImportService;
        private readonly IMessageDialog _messageDialog;
        private readonly IVGMMusicPlayer _musicPlayer;
        private readonly ReadOnlyObservableCollection<BgmPropertyEntryViewModel> _bgmPropertyEntries;
        private const string REGEX_REPLACE = @"[^a-zA-Z0-9_]";
        private const string REGEX_VALIDATION = @"^[a-z0-9_]+$";
        private bool _isUpdatingLoopFields;
        private string _loopPreviewFile;

        public ReactiveCommand<Window, Unit> ActionCancel { get; }
        public ReactiveCommand<Window, Unit> ActionCreate { get; }
        public ReactiveCommand<Unit, Unit> ActionPreviewLoop { get; }
        public ReactiveCommand<Unit, Unit> ActionStopPreview { get; }

        [Reactive]
        public string Filename { get; set; }

        [Reactive]
        public string ToneId { get; set; }

        [Reactive]
        public bool IsAudioImport { get; set; }

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
        public double WindowHeight { get; set; }

        public MusicModEntries NewMusicModEntries { get; private set; }

        public ToneIdCreationModalWindowModel(ILogger<ToneIdCreationModalWindowModel> logger, IViewModelManager viewModelManager, IAudioImportService audioImportService, IMessageDialog messageDialog, IVGMMusicPlayer musicPlayer)
        {
            _logger = logger;
            _audioImportService = audioImportService;
            _messageDialog = messageDialog;
            _musicPlayer = musicPlayer;
            WindowHeight = 400;

            //Bind observables
            viewModelManager.ObservableBgmPropertyEntries.Connect()
               .ObserveOn(RxApp.MainThreadScheduler)
               .Bind(out _bgmPropertyEntries)
               .DisposeMany()
               .Subscribe();

            this.ValidationRule(p => p.ToneId,
                p => !string.IsNullOrEmpty(p) && Regex.IsMatch(p, REGEX_VALIDATION),
                $"The ToneId can only contain lowercase letters, digits and underscore.");

            this.ValidationRule(p => p.ToneId,
              p => p != null && p.Length <= MusicConstants.GameResources.ToneIdMaximumSize,
              $"The ToneId is too long. Maximum is {MusicConstants.GameResources.ToneIdMaximumSize}");

            this.ValidationRule(p => p.ToneId,
             p => p != null && p.Length >= MusicConstants.GameResources.ToneIdMinimumSize,
             $"The ToneId is too short. Minimum is {MusicConstants.GameResources.ToneIdMinimumSize}");

            this.ValidationRule(p => p.ToneId,
               p => !string.IsNullOrEmpty(p) && !_bgmPropertyEntries.Select(p2 => p2.NameId).Contains(p),
               $"The ToneId already exists in the database");

            this.ValidationRule(p => p.LoopEndSample,
                p => !IsAudioImport || p > 0,
                "Loop end sample must be greater than 0.");

            this.ValidationRule(p => p.LoopStartSample,
                p => !IsAudioImport || p <= LoopEndSample,
                "Loop start sample must be lower than or equal to loop end sample.");

            this.ValidationRule(p => p.LoopEndSample,
                p => !IsAudioImport || LoopStartSample <= p,
                "Loop end sample must be greater than or equal to loop start sample.");

            this.ValidationRule(p => p.LoopEndSample,
                p => !IsAudioImport || p <= TotalSamples,
                "Loop end sample cannot be greater than the total sample count.");

            this.ValidationRule(p => p.LoopEndMs,
                p => !IsAudioImport || p > 0,
                "Loop end ms must be greater than 0.");

            this.ValidationRule(p => p.LoopStartMs,
                p => !IsAudioImport || p <= LoopEndMs,
                "Loop start ms must be lower than or equal to loop end ms.");

            this.ValidationRule(p => p.LoopEndMs,
                p => !IsAudioImport || LoopStartMs <= p,
                "Loop end ms must be greater than or equal to loop start ms.");

            this.ValidationRule(p => p.LoopEndMs,
                p => !IsAudioImport || p <= TotalTimeMs,
                "Loop end ms cannot be greater than the total length.");

            var canExecute = this.WhenAnyValue(x => x.ValidationContext.IsValid);
            var canPreview = this.WhenAnyValue(x => x.IsAudioImport, x => x.ValidationContext.IsValid, (isAudioImport, isValid) => isAudioImport && isValid);
            ActionCancel = ReactiveCommand.Create<Window>(Cancel);
            ActionCreate = ReactiveCommand.Create<Window>(Select, canExecute);
            ActionPreviewLoop = ReactiveCommand.CreateFromTask(PreviewLoop, canPreview);
            ActionStopPreview = ReactiveCommand.CreateFromTask(StopPreview);

            this.WhenAnyValue(p => p.LoopStartSample)
                .Subscribe(p => UpdateLoopStartMsFromSample(p));

            this.WhenAnyValue(p => p.LoopEndSample)
                .Subscribe(p => UpdateLoopEndMsFromSample(p));

            this.WhenAnyValue(p => p.LoopStartMs)
                .Subscribe(p => UpdateLoopStartSampleFromMs(p));

            this.WhenAnyValue(p => p.LoopEndMs)
                .Subscribe(p => UpdateLoopEndSampleFromMs(p));
        }

        public void LoadToneId(string toneId)
        {
            ToneId = Regex.Replace(toneId.Replace(" ", "_"), REGEX_REPLACE, string.Empty).ToLower();
        }

        public void LoadAudioImportInfo(uint sampleRate, uint totalSamples)
        {
            IsAudioImport = true;
            WindowHeight = 700;
            SampleRate = sampleRate;
            TotalSamples = totalSamples;
            TotalTimeMs = SamplesToMs(totalSamples);
            LoopStartSample = 0;
            LoopEndSample = totalSamples;
        }

        public void ClearAudioImportInfo()
        {
            IsAudioImport = false;
            WindowHeight = 400;
            SampleRate = 0;
            TotalSamples = 0;
            TotalTimeMs = 0;
            LoopStartSample = 0;
            LoopEndSample = 0;
            LoopStartMs = 0;
            LoopEndMs = 0;
        }

        private async void Cancel(Window w)
        {
            try
            {
                _logger.LogInformation("Tone ID modal cancel requested. Stopping preview before close.");
                await StopPreview();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error while stopping loop preview on cancel.");
            }
            w.Close();
        }

        private async void Select(Window window)
        {
            _logger.LogDebug("Clicked OK");
            try
            {
                _logger.LogInformation("Tone ID modal choose requested. Stopping preview before close.");
                await StopPreview();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error while stopping loop preview on choose.");
            }
            window.Close(window);
        }

        private async Task PreviewLoop()
        {
            try
            {
                _logger.LogInformation("Preview loop clicked. Filename={Filename}, LoopStartSample={LoopStartSample}, LoopEndSample={LoopEndSample}, LoopStartMs={LoopStartMs}, LoopEndMs={LoopEndMs}, TotalSamples={TotalSamples}",
                    Filename, LoopStartSample, LoopEndSample, LoopStartMs, LoopEndMs, TotalSamples);

                await StopPreview();

                var previewInfo = await _audioImportService.CreateLoopPreview(Filename, LoopStartSample, LoopEndSample, TotalSamples);
                _loopPreviewFile = previewInfo.Filename;
                _logger.LogInformation("Preview loop file ready. File={PreviewFile}, StartSample={StartSample}, Exists={Exists}, Length={Length}",
                    previewInfo.Filename, previewInfo.StartSample, File.Exists(previewInfo.Filename), File.Exists(previewInfo.Filename) ? new FileInfo(previewInfo.Filename).Length : 0);

                var played = await _musicPlayer.Play(previewInfo.Filename, previewInfo.StartSample);
                _logger.LogInformation("Preview loop play requested. Played={Played}", played);
                if (!played)
                    await _messageDialog.ShowError("Loop preview failed", "The preview file was created, but vgmstream could not play it.");
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Loop preview failed.");
                await _messageDialog.ShowError("Loop preview failed", e.Message, e);
            }
        }

        private async Task StopPreview()
        {
            _logger.LogInformation("StopPreview starting. PreviewFile={PreviewFile}", _loopPreviewFile);

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
                try
                {
                    if (File.Exists(_loopPreviewFile))
                    {
                        File.Delete(_loopPreviewFile);
                        _logger.LogInformation("Deleted loop preview file {LoopPreviewFile}.", _loopPreviewFile);
                    }
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e, "Could not delete loop preview file {LoopPreviewFile}.", _loopPreviewFile);
                }

                _loopPreviewFile = null;
            }

            _logger.LogInformation("StopPreview completed.");
        }

        private void UpdateLoopStartMsFromSample(uint sample)
        {
            if (_isUpdatingLoopFields)
                return;

            UpdateLoopFields(() => LoopStartMs = SamplesToMs(sample));
        }

        private void UpdateLoopEndMsFromSample(uint sample)
        {
            if (_isUpdatingLoopFields)
                return;

            UpdateLoopFields(() => LoopEndMs = SamplesToMs(sample));
        }

        private void UpdateLoopStartSampleFromMs(uint milliseconds)
        {
            if (_isUpdatingLoopFields)
                return;

            UpdateLoopFields(() => LoopStartSample = MsToSamples(milliseconds));
        }

        private void UpdateLoopEndSampleFromMs(uint milliseconds)
        {
            if (_isUpdatingLoopFields)
                return;

            UpdateLoopFields(() => LoopEndSample = MsToSamples(milliseconds));
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
            return SampleRate == 0 ? 0 : (uint)Math.Round(milliseconds * SampleRate / 1000.0);
        }
    }
}
