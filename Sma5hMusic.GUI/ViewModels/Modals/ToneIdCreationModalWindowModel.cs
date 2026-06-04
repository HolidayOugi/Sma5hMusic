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
using Sma5hMusic.GUI.Models;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using VGMMusic;

namespace Sma5hMusic.GUI.ViewModels
{
    public partial class ToneIdCreationModalWindowModel : ReactiveValidationObject
    {
        private readonly ILogger _logger;
        private readonly ReadOnlyObservableCollection<BgmPropertyEntryViewModel> _bgmPropertyEntries;
        private const string REGEX_REPLACE = @"[^a-zA-Z0-9_]";
        private const string REGEX_VALIDATION = @"^[a-z0-9_]+$";

        public ReactiveCommand<Window, Unit> ActionCancel { get; }
        public ReactiveCommand<Window, Unit> ActionCreate { get; }

        [Reactive]
        public string Filename { get; set; }

        [Reactive]
        public string ToneId { get; set; }

        public MusicModEntries NewMusicModEntries { get; private set; }

        public ToneIdCreationModalWindowModel(ILogger<ToneIdCreationModalWindowModel> logger, IViewModelManager viewModelManager, IAudioImportService audioImportService, IMessageDialog messageDialog, IVGMMusicPlayer musicPlayer)
        {
            _logger = logger;
            _audioImportService = audioImportService;
            _messageDialog = messageDialog;
            _musicPlayer = musicPlayer;
            WindowHeight = 400;
            WindowWidth = 520;
            WindowMinWidth = 500;
            PreviewProgressText = string.Empty;
            AutoLoopStatus = string.Empty;
            AutoLoopPoints = new ObservableCollection<AutoLoopPoint>();

            //Bind observables
            this.WhenAnyValue(x => x.IsAudioImport)
                .Subscribe(_ => this.RaisePropertyChanged(nameof(WindowTitle)));

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
                this.WhenAnyValue(p => p.IsAudioImport, p => p.LoopStartSample, p => p.LoopEndSample,
                    (isAudioImport, loopStartSample, loopEndSample) => !isAudioImport || loopStartSample <= loopEndSample),
                "Loop start sample must be lower than or equal to loop end sample.");

            this.ValidationRule(p => p.LoopEndSample,
                this.WhenAnyValue(p => p.IsAudioImport, p => p.LoopStartSample, p => p.LoopEndSample,
                    (isAudioImport, loopStartSample, loopEndSample) => !isAudioImport || loopStartSample <= loopEndSample),
                "Loop end sample must be greater than or equal to loop start sample.");

            this.ValidationRule(p => p.LoopEndSample,
                p => !IsAudioImport || p <= TotalSamples,
                "Loop end sample cannot be greater than the total sample count.");

            this.ValidationRule(p => p.LoopEndMs,
                p => !IsAudioImport || p > 0,
                "Loop end ms must be greater than 0.");

            this.ValidationRule(p => p.LoopStartMs,
                this.WhenAnyValue(p => p.IsAudioImport, p => p.LoopStartMs, p => p.LoopEndMs,
                    (isAudioImport, loopStartMs, loopEndMs) => !isAudioImport || loopStartMs <= loopEndMs),
                "Loop start ms must be lower than or equal to loop end ms.");

            this.ValidationRule(p => p.LoopEndMs,
                this.WhenAnyValue(p => p.IsAudioImport, p => p.LoopStartMs, p => p.LoopEndMs,
                    (isAudioImport, loopStartMs, loopEndMs) => !isAudioImport || loopStartMs <= loopEndMs),
                "Loop end ms must be greater than or equal to loop start ms.");

            this.ValidationRule(p => p.LoopEndMs,
                p => !IsAudioImport || p <= TotalTimeMs,
                "Loop end ms cannot be greater than the total length.");

            var canExecute = this.WhenAnyValue(x => x.ValidationContext.IsValid);
            var canPreview = this.WhenAnyValue(
                x => x.IsAudioImport,
                x => x.LoopStartSample,
                x => x.LoopEndSample,
                x => x.TotalSamples,
                (isAudioImport, loopStartSample, loopEndSample, totalSamples) =>
                    isAudioImport &&
                    loopEndSample > 0 &&
                    loopStartSample <= loopEndSample &&
                    loopEndSample <= totalSamples);
            var canCalculateAutoLoops = this.WhenAnyValue(x => x.IsAudioImport, x => x.IsCalculatingAutoLoops, (isAudioImport, isCalculating) => isAudioImport && !isCalculating);
            ActionCancel = ReactiveCommand.Create<Window>(Cancel);
            ActionCreate = ReactiveCommand.Create<Window>(Select, canExecute);
            ActionPreviewLoop = ReactiveCommand.CreateFromTask(PreviewLoop, canPreview);
            ActionStopPreview = ReactiveCommand.CreateFromTask(StopPreview);
            ActionResetLoopDefaults = ReactiveCommand.Create(ResetLoopDefaults);
            ActionCalculateAutoLoops = ReactiveCommand.CreateFromTask(CalculateAutoLoops, canCalculateAutoLoops);
            ActionLoadMoreAutoLoops = ReactiveCommand.Create(LoadMoreAutoLoops);
            ActionPreviewAutoLoop = ReactiveCommand.CreateFromTask<AutoLoopPoint>(PreviewAutoLoop);

            this.WhenAnyValue(p => p.LoopStartSample)
                .Subscribe(p => UpdateLoopStartMsFromSample(p));

            this.WhenAnyValue(p => p.LoopEndSample)
                .Subscribe(p => UpdateLoopEndMsFromSample(p));

            this.WhenAnyValue(p => p.LoopStartMs)
                .Subscribe(p => UpdateLoopStartSampleFromMs(p));

            this.WhenAnyValue(p => p.LoopEndMs)
                .Subscribe(p => UpdateLoopEndSampleFromMs(p));

            this.WhenAnyValue(p => p.LoopStartMinutes, p => p.LoopStartSeconds, p => p.LoopStartMilliseconds)
                .Subscribe(_ => UpdateLoopStartFromTimeParts());

            this.WhenAnyValue(p => p.LoopEndMinutes, p => p.LoopEndSeconds, p => p.LoopEndMilliseconds)
                .Subscribe(_ => UpdateLoopEndFromTimeParts());

            this.WhenAnyValue(p => p.SelectedAutoLoop)
                .Where(p => p != null)
                .Subscribe(ApplyAutoLoop);
        }

        public void LoadToneId(string toneId)
        {
            var sanitizedToneId = Regex.Replace(toneId.Replace(" ", "_"), REGEX_REPLACE, string.Empty).ToLower();
            ToneId = string.IsNullOrEmpty(sanitizedToneId) ? Guid.NewGuid().ToString("N") : sanitizedToneId;
        }

        private async void Cancel(Window w)
        {
            try
            {
                _logger.LogInformation("Tone ID modal cancel requested. Stopping preview before close.");
                await ClosePreview();
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
                await ClosePreview();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error while stopping loop preview on choose.");
            }
            window.Close(window);
        }
    }
}
