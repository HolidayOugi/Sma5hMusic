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
using System.Collections.Generic;
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
        private const int AutoLoopPageSize = 15;
        private bool _isUpdatingLoopFields;
        private string _loopPreviewFile;
        private int _loopPreviewVersion;
        private bool _isCompletingPreview;
        private IDisposable _previewProgressSubscription;
        private IDisposable _autoLoopStatusSubscription;
        private LoopPreviewInfo _loopPreviewInfo;
        private List<AutoLoopPoint> _allAutoLoopPoints = new List<AutoLoopPoint>();

        public ReactiveCommand<Window, Unit> ActionCancel { get; }
        public ReactiveCommand<Window, Unit> ActionCreate { get; }
        public ReactiveCommand<Unit, Unit> ActionPreviewLoop { get; }
        public ReactiveCommand<Unit, Unit> ActionStopPreview { get; }
        public ReactiveCommand<Unit, Unit> ActionCalculateAutoLoops { get; }
        public ReactiveCommand<Unit, Unit> ActionLoadMoreAutoLoops { get; }
        public ReactiveCommand<AutoLoopPoint, Unit> ActionPreviewAutoLoop { get; }

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

        [Reactive]
        public bool IsPreviewProgressVisible { get; set; }

        [Reactive]
        public bool IsCalculatingAutoLoops { get; set; }

        [Reactive]
        public bool IsAutoLoopCandidatesVisible { get; set; }

        [Reactive]
        public AutoLoopPoint SelectedAutoLoop { get; set; }

        [Reactive]
        public string AutoLoopStatus { get; set; }

        [Reactive]
        public bool HasMoreAutoLoops { get; set; }

        [Reactive]
        public double PreviewProgressMaximum { get; set; }

        [Reactive]
        public double PreviewProgressValue { get; set; }

        [Reactive]
        public string PreviewProgressText { get; set; }

        public MusicModEntries NewMusicModEntries { get; private set; }
        public ObservableCollection<AutoLoopPoint> AutoLoopPoints { get; }

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
            var canPreview = this.WhenAnyValue(x => x.IsAudioImport, x => x.ValidationContext.IsValid, (isAudioImport, isValid) => isAudioImport && isValid);
            var canCalculateAutoLoops = this.WhenAnyValue(x => x.IsAudioImport, x => x.IsCalculatingAutoLoops, (isAudioImport, isCalculating) => isAudioImport && !isCalculating);
            ActionCancel = ReactiveCommand.Create<Window>(Cancel);
            ActionCreate = ReactiveCommand.Create<Window>(Select, canExecute);
            ActionPreviewLoop = ReactiveCommand.CreateFromTask(PreviewLoop, canPreview);
            ActionStopPreview = ReactiveCommand.CreateFromTask(StopPreview);
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
            ToneId = Regex.Replace(toneId.Replace(" ", "_"), REGEX_REPLACE, string.Empty).ToLower();
        }

        public void LoadAudioImportInfo(uint sampleRate, uint totalSamples)
        {
            IsAudioImport = true;
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

        public void ClearAudioImportInfo()
        {
            IsAudioImport = false;
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

        private async Task CalculateAutoLoops()
        {
            try
            {
                _logger.LogInformation("Calculate automatic loop points clicked. Filename={Filename}, SampleRate={SampleRate}, TotalSamples={TotalSamples}.",
                    Filename, SampleRate, TotalSamples);

                await StopPreview();
                ClearAutoLoopPoints();
                IsCalculatingAutoLoops = true;
                StartAutoLoopStatusAnimation();

                var loopPoints = await _audioImportService.CalculateAutoLoopPoints(Filename, SampleRate, TotalSamples);
                if (loopPoints.Count == 0)
                {
                    _logger.LogInformation("pymusiclooper did not return any valid loop points for {Filename}.", Filename);
                    AutoLoopStatus = "No automatic loop points found.";
                    await _messageDialog.ShowInformation("No loop points found", "pymusiclooper did not find any usable loop points for this audio file.");
                    return;
                }

                _allAutoLoopPoints = loopPoints.ToList();
                LoadMoreAutoLoops();

                IsAutoLoopCandidatesVisible = true;
                SelectedAutoLoop = AutoLoopPoints.FirstOrDefault();
                UpdateAutoLoopLoadedStatus();
                _logger.LogInformation("Automatic loop point candidates loaded into modal. Count={Count}, SelectedRank={SelectedRank}.",
                    _allAutoLoopPoints.Count, SelectedAutoLoop?.Rank);
            }
            catch (FileNotFoundException e)
            {
                _logger.LogError(e, "pymusiclooper is not available.");
                AutoLoopStatus = "pymusiclooper is not available.";
                await _messageDialog.ShowError("pymusiclooper not found", "pymusiclooper is not installed or is not available in PATH.", e);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Automatic loop point calculation failed.");
                AutoLoopStatus = "Automatic loop point calculation failed.";
                await _messageDialog.ShowError("Automatic loop point calculation failed", e.Message, e);
            }
            finally
            {
                IsCalculatingAutoLoops = false;
                StopAutoLoopStatusAnimation();
            }
        }

        private async Task PreviewAutoLoop(AutoLoopPoint loopPoint)
        {
            if (loopPoint == null)
                return;

            _logger.LogInformation("Automatic loop preview clicked. Rank={Rank}, Start={Start}, End={End}, Score={Score}.",
                loopPoint.Rank, loopPoint.LoopStartSample, loopPoint.LoopEndSample, loopPoint.Score);

            SelectedAutoLoop = loopPoint;
            ApplyAutoLoop(loopPoint);
            await PreviewLoop();
        }

        private void ApplyAutoLoop(AutoLoopPoint loopPoint)
        {
            if (loopPoint == null)
                return;

            _logger.LogInformation("Applying automatic loop point. Rank={Rank}, Start={Start}, End={End}, Score={Score}.",
                loopPoint.Rank, loopPoint.LoopStartSample, loopPoint.LoopEndSample, loopPoint.Score);

            LoopStartSample = loopPoint.LoopStartSample;
            LoopEndSample = loopPoint.LoopEndSample;
            AutoLoopStatus = $"Selected automatic loop #{loopPoint.Rank} ({loopPoint.ScoreText}).";
        }

        private void ClearAutoLoopPoints()
        {
            _allAutoLoopPoints.Clear();
            AutoLoopPoints.Clear();
            SelectedAutoLoop = null;
            IsAutoLoopCandidatesVisible = false;
            HasMoreAutoLoops = false;
            AutoLoopStatus = string.Empty;
        }

        private void LoadMoreAutoLoops()
        {
            var nextLoopPoints = _allAutoLoopPoints
                .Skip(AutoLoopPoints.Count)
                .Take(AutoLoopPageSize)
                .ToList();

            foreach (var loopPoint in nextLoopPoints)
                AutoLoopPoints.Add(loopPoint);

            HasMoreAutoLoops = AutoLoopPoints.Count < _allAutoLoopPoints.Count;
            IsAutoLoopCandidatesVisible = AutoLoopPoints.Count > 0;
            UpdateAutoLoopLoadedStatus();
            _logger.LogInformation("Loaded automatic loop point page. Visible={VisibleCount}, Total={TotalCount}, HasMore={HasMore}.",
                AutoLoopPoints.Count, _allAutoLoopPoints.Count, HasMoreAutoLoops);
        }

        private void UpdateAutoLoopLoadedStatus()
        {
            if (_allAutoLoopPoints.Count > 0)
                AutoLoopStatus = $"Showing {AutoLoopPoints.Count} of {_allAutoLoopPoints.Count} automatic loop point candidate(s).";
        }

        private void StartAutoLoopStatusAnimation()
        {
            StopAutoLoopStatusAnimation();
            UpdateAutoLoopStatusAnimation(0);
            _autoLoopStatusSubscription = Observable.Interval(TimeSpan.FromSeconds(1))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(p => UpdateAutoLoopStatusAnimation((int)((p + 1) % 4)));
        }

        private void StopAutoLoopStatusAnimation()
        {
            _autoLoopStatusSubscription?.Dispose();
            _autoLoopStatusSubscription = null;
        }

        private void UpdateAutoLoopStatusAnimation(int dotCount)
        {
            AutoLoopStatus = $"Calculating automatic loop points{new string('.', dotCount)}";
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
            PreviewProgressText = $"Preview: {FormatMs(SamplesToMs((uint)PreviewProgressValue))} / {FormatMs(LoopEndMs)} - loop from {FormatMs(LoopEndMs)} to {FormatMs(LoopStartMs)}";
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
